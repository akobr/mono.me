using System.ComponentModel.DataAnnotations;
using System.IO.Abstractions;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;

namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// Fills the options the configure callback left unset from the environment, validates them once and binds the
// credential. Code wins over the environment. Messages name the variable or property, never a secret.
internal static class SettingsReader
{
    private const string OptionsName = nameof(StorytellerConfigurationOptions);

    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(1);

    // With the jitter of up to 10 %, the longest interval whose delay a system timer takes (about 49.7 days).
    private static readonly TimeSpan MaximumRefreshInterval = TimeSpan.FromDays(45);

    // requireKey: the provider needs a key; the document service takes one as its default scope only.
    public static StorytellerSettings Read(StorytellerConfigurationOptions options, bool requireKey)
    {
        return Read(options, requireKey, isProvider: true);
    }

    // The document service: Key is optional, and the options of the provider alone (RefreshInterval, Optional,
    // CacheDirectory, CacheProtector and Placement) are ignored, from code and from the environment. A federated
    // credential exchanges at the token endpoint of the project its own options name, or else of the project of Key, so
    // one that names none fails its reads without Key and names the fix.
    public static StorytellerSettings ReadForDocuments(StorytellerConfigurationOptions options)
    {
        return Read(options, requireKey: false, isProvider: false);
    }

    private static StorytellerSettings Read(StorytellerConfigurationOptions options, bool requireKey, bool isProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        var variables = new VariableReader(options.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable);
        var endpoint = ReadEndpoint(options, variables);
        var (key, keyText) = ReadKey(options, variables, requireKey);
        var timeProvider = options.TimeProvider ?? TimeProvider.System;
        var fileSystem = options.FileSystem ?? new FileSystem();
        var resilience = CopyResilience(options.Resilience, keyText);
        var credential = ReadCredential(options, variables, fileSystem, timeProvider, keyText);

        if (isProvider && !Enum.IsDefined(options.Placement))
        {
            throw Invalid($"{OptionsName}.{nameof(options.Placement)} is not a placement.", keyText);
        }

        return new StorytellerSettings
        {
            Endpoint = endpoint,
            Key = key,
            Mode = ReadMode(options, variables, keyText),
            RefreshInterval = isProvider ? ReadRefreshInterval(options, variables, keyText) : TimeSpan.Zero,
            Optional = isProvider && ReadOptional(options, variables, keyText),
            CacheDirectory = isProvider ? ReadCacheDirectory(options, variables, fileSystem, keyText) : null,
            Credential = credential?.Bind(new StorytellerCredentialBinding(endpoint)
            {
                Organization = key?.OrganizationName,
                Project = key?.ProjectName,
                ConfigureHandler = options.ConfigurePrimaryHandler,
                TimeProvider = timeProvider,
                Resilience = resilience,
            }),
            ReferenceResolver = options.ReferenceResolver,
            SecretRefreshInterval = RequirePositive(options.SecretRefreshInterval, nameof(options.SecretRefreshInterval), keyText),
            ReadTimeout = RequirePositive(options.ReadTimeout, nameof(options.ReadTimeout), keyText),
            Resilience = resilience,
            Placement = isProvider ? options.Placement : SourcePlacement.BeforeEnvironmentVariables,
            CacheProtector = isProvider ? options.CacheProtector : null,
            LoggerFactory = options.LoggerFactory,
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ConfigurePrimaryHandler = options.ConfigurePrimaryHandler,
            PrimaryHandler = options.PrimaryHandler,
            NextRandom = options.NextRandom ?? Random.Shared.NextDouble,
        };
    }

    private static Uri ReadEndpoint(StorytellerConfigurationOptions options, VariableReader variables)
    {
        var (text, source) = FromCode(options.Endpoint, nameof(options.Endpoint))
            ?? (variables.ReadWithFallback(ConfigurationVariables.ENDPOINT, ConfigurationVariables.API_URL, out var variable), variable);

        if (text is null)
        {
            throw Invalid(
                $"The Storyteller endpoint is not set: set {ConfigurationVariables.ENDPOINT} (or {ConfigurationVariables.API_URL}) or {OptionsName}.{nameof(options.Endpoint)} to the API base, /api included.",
                null);
        }

        // The value is not printed: it may carry user info.
        if (!Uri.TryCreate(text, UriKind.Absolute, out var endpoint) || !LocalUrl.IsAllowed(endpoint))
        {
            throw Invalid($"{source} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.", null);
        }

        // Paths are appended to it.
        if (endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
        {
            throw Invalid($"{source} must not have a query or a fragment; set the API base, /api included.", null);
        }

        return endpoint;
    }

    private static (FullKey? Key, string? Text) ReadKey(StorytellerConfigurationOptions options, VariableReader variables, bool requireKey)
    {
        var (text, source) = FromCode(options.Key, nameof(options.Key))
            ?? (variables.Read(ConfigurationVariables.KEY), ConfigurationVariables.KEY);

        if (text is null)
        {
            return requireKey
                ? throw Invalid($"The Storyteller key is not set: set {ConfigurationVariables.KEY} or {OptionsName}.{nameof(options.Key)} to the full key of the configuration, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.", null)
                : (null, null);
        }

        try
        {
            return (FullKey.Parse(text), text);
        }
        catch (FormatException exception)
        {
            throw Invalid($"{source} must be a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}. {exception.Message}", text, exception);
        }
    }

    private static StorytellerReadMode ReadMode(StorytellerConfigurationOptions options, VariableReader variables, string? key)
    {
        if (options.Mode is { } mode)
        {
            return Enum.IsDefined(mode)
                ? mode
                : throw Invalid($"{OptionsName}.{nameof(options.Mode)} is not a read mode.", key);
        }

        var text = variables.Read(ConfigurationVariables.MODE);

        if (text is null)
        {
            return StorytellerReadMode.Resolved;
        }

        return SettingValues.TryParseMode(text, out var parsed)
            ? parsed
            : throw Invalid($"{ConfigurationVariables.MODE} must be resolved or effective.", key);
    }

    private static TimeSpan ReadRefreshInterval(StorytellerConfigurationOptions options, VariableReader variables, string? key)
    {
        string source;
        TimeSpan interval;

        if (options.RefreshInterval is { } fromCode)
        {
            source = $"{OptionsName}.{nameof(options.RefreshInterval)}";
            interval = fromCode;
        }
        else if (variables.Read(ConfigurationVariables.REFRESH_INTERVAL) is { } text)
        {
            source = ConfigurationVariables.REFRESH_INTERVAL;

            if (!SettingValues.TryParseDuration(text, out interval))
            {
                throw Invalid($"{source} must be a duration: 500ms, 30s, 5m, 1h, plain seconds or TimeSpan text (00:00:30).", key);
            }
        }
        else
        {
            return DefaultRefreshInterval;
        }

        // A poller that hammers the platform helps nobody; zero is the explicit way to turn it off.
        return interval == TimeSpan.Zero || (interval >= MinimumRefreshInterval && interval <= MaximumRefreshInterval)
            ? interval
            : throw Invalid($"{source} must be 0, which disables the refresh, or between one second and {MaximumRefreshInterval.TotalDays} days.", key);
    }

    private static bool ReadOptional(StorytellerConfigurationOptions options, VariableReader variables, string? key)
    {
        if (options.Optional is { } optional)
        {
            return optional;
        }

        var text = variables.Read(ConfigurationVariables.OPTIONAL);

        if (text is null)
        {
            return false;
        }

        return SettingValues.TryParseBoolean(text, out var parsed)
            ? parsed
            : throw Invalid($"{ConfigurationVariables.OPTIONAL} must be true, false, 1 or 0.", key);
    }

    private static string? ReadCacheDirectory(StorytellerConfigurationOptions options, VariableReader variables, IFileSystem fileSystem, string? key)
    {
        var (text, source) = FromCode(options.CacheDirectory, nameof(options.CacheDirectory))
            ?? (variables.Read(ConfigurationVariables.CACHE_DIR), ConfigurationVariables.CACHE_DIR);

        if (text is null)
        {
            return null;
        }

        // Resolved now: a later change of the working directory must not move the cache.
        try
        {
            return fileSystem.Path.GetFullPath(text);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw Invalid($"{source} is not a valid directory path.", key, exception);
        }
    }

    private static StorytellerCredential? ReadCredential(
        StorytellerConfigurationOptions options,
        VariableReader variables,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        string? key)
    {
        if (options.Credential is not null)
        {
            return options.Credential;
        }

        try
        {
            return StorytellerCredential.FromEnvironment(variables.GetVariable, fileSystem, timeProvider);
        }
        catch (StorytellerAuthenticationException exception)
        {
            // The SDK's messages name variables and paths, never a secret.
            throw Invalid(exception.Message, key, exception);
        }
    }

    // A copy, validated by building the pipeline the transport builds later, so a bad value fails when the source is
    // added and not at the first read.
    private static StorytellerResilienceOptions CopyResilience(StorytellerResilienceOptions? resilience, string? key)
    {
        if (resilience is null)
        {
            throw Invalid($"{OptionsName}.{nameof(StorytellerConfigurationOptions.Resilience)} is null.", key);
        }

        var copy = new StorytellerResilienceOptions
        {
            MaxRetryAttempts = resilience.MaxRetryAttempts,
            BaseDelay = resilience.BaseDelay,
            AttemptTimeout = resilience.AttemptTimeout,
            RetryUnsafeMethods = resilience.RetryUnsafeMethods,
            FailureRatio = resilience.FailureRatio,
            MinimumThroughput = resilience.MinimumThroughput,
            SamplingDuration = resilience.SamplingDuration,
            BreakDuration = resilience.BreakDuration,
        };

        try
        {
            StorytellerResilience.CreatePipeline(copy);
        }
        catch (Exception exception) when (exception is ValidationException or ArgumentException)
        {
            throw Invalid($"{OptionsName}.{nameof(StorytellerConfigurationOptions.Resilience)} is not valid: {exception.Message}", key, exception);
        }

        return copy;
    }

    // Infinite is allowed: no limit.
    private static TimeSpan RequirePositive(TimeSpan value, string name, string? key)
    {
        return value > TimeSpan.Zero || value == Timeout.InfiniteTimeSpan
            ? value
            : throw Invalid($"{OptionsName}.{name} must be positive.", key);
    }

    // A value set in code, with the name of its property; null when the callback left it unset or blank.
    private static (string? Text, string Source)? FromCode(string? value, string property)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : (value.Trim(), $"{OptionsName}.{property}");
    }

    private static StorytellerConfigurationException Invalid(string message, string? key, Exception? innerException = null)
    {
        return new StorytellerConfigurationException(message, key, null, null, false, innerException);
    }
}
