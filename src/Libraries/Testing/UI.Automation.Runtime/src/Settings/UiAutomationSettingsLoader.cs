using System.Collections;
using System.IO.Abstractions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace _42.Testing.UI.Automation.Runtime.Settings;

// Reads the settings in layers, each over the one before:
//   1. testsettings.json next to the test assembly (committed defaults);
//   2. testsettings.<profile>.json for each profile named by the options, the Profile parameter or UITESTS_PROFILE;
//   3. testsettings.local.json (git-ignored overrides for the IDE);
//   4. the user secrets of the test assembly;
//   5. UITESTS_* environment variables, "__" separating sections (UITESTS_Target__BaseUrl);
//   6. the engine's parameters.
// Users:Pool is not merged by index across the layers: the highest layer that sets any Users:Pool:<n>:Email defines
// the pool (its entries, nothing of the lower layers' entries), and Users:Pool:<n>:Password from any layer (typically the
// user secrets) applies by index onto it; a password of an index the pool lacks is ignored. Without any Email the
// layers merge as usual.
// A missing profile file or a file that is not JSON fails at once, also without a target. Without Target:BaseUrl the
// suite is in skip mode and nothing else is validated (except Reporting for a replay); otherwise every error is
// collected and thrown together.
public static class UiAutomationSettingsLoader
{
    public const string EnvironmentPrefix = "UITESTS_";
    public const string ProfileVariable = "UITESTS_PROFILE";
    public const string ProfileParameter = "Profile";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static UiAutomationSettings Load(UiAutomationSettingsOptions? options = null)
    {
        options ??= new UiAutomationSettingsOptions();

        var fileSystem = options.FileSystem ?? new FileSystem();
        var baseDirectory = options.BaseDirectory ?? AppContext.BaseDirectory;
        var environment = options.EnvironmentVariables ?? ReadProcessEnvironment();
        var parameters = NormalizeParameters(options.Parameters);
        var profile = ResolveProfile(options.Profile, parameters, environment);

        var builder = new ConfigurationBuilder();
        var sources = new List<string>();
        var errors = new List<string>();

        AddJsonFile(builder, fileSystem, baseDirectory, $"{options.FileName}.json", null, sources, errors);
        foreach (var name in SplitProfiles(profile))
        {
            AddJsonFile(builder, fileSystem, baseDirectory, $"{options.FileName}.{name}.json", name, sources, errors);
        }

        AddJsonFile(builder, fileSystem, baseDirectory, $"{options.FileName}.local.json", null, sources, errors);
        AddUserSecrets(builder, fileSystem, options, sources, errors);
        AddEnvironment(builder, environment, sources);

        if (parameters.Count > 0)
        {
            builder.AddInMemoryCollection(parameters);
            sources.Add("parameters");
        }

        if (errors.Count > 0)
        {
            throw new UiAutomationSettingsException(errors, sources);
        }

        var configuration = builder.Build();
        var bindErrors = new List<(string Section, string Error)>();
        var settings = new UiAutomationSettings(configuration, baseDirectory)
        {
            Profile = profile,
            Sources = sources,
            Target = BindSection<TargetSettings>(configuration, "Target", bindErrors),
            Browser = BindSection<BrowserSettings>(configuration, "Browser", bindErrors),
            Execution = BindSection<ExecutionSettings>(configuration, "Execution", bindErrors),
            Users = BindSection<UsersSettings>(configuration, "Users", bindErrors),
            BrowserStack = BindSection<BrowserStackSettings>(configuration, "BrowserStack", bindErrors),
            Reporting = BindSection<ReportingSettings>(configuration, "Reporting", bindErrors),
        };

        if (ResolvePool(configuration) is { } pool)
        {
            settings.Users.Pool = BindSection<UsersSettings>(pool, "Users", bindErrors).Pool;
        }

        if (settings.IsConfigured)
        {
            errors.AddRange(bindErrors.Select(error => error.Error));
            errors.AddRange(SettingsValidator.Validate(settings, options.Setup));
        }
        else if (settings.IsReplay)
        {
            errors.AddRange(bindErrors.Where(error => error.Section == "Reporting").Select(error => error.Error));
            errors.AddRange(SettingsValidator.ValidateReporting(settings.Reporting));
        }

        if (errors.Count > 0)
        {
            throw new UiAutomationSettingsException(errors, sources);
        }

        if (settings.IsConfigured)
        {
            SettingsValidator.Adjust(settings);
        }

        return settings;
    }

    private static string? ResolveProfile(string? profile, IReadOnlyDictionary<string, string?> parameters, IReadOnlyDictionary<string, string?> environment)
    {
        if (!string.IsNullOrWhiteSpace(profile))
        {
            return profile.Trim();
        }

        if (parameters.TryGetValue(ProfileParameter, out var parameter) && !string.IsNullOrWhiteSpace(parameter))
        {
            return parameter.Trim();
        }

        var variable = environment.FirstOrDefault(pair => string.Equals(pair.Key, ProfileVariable, StringComparison.OrdinalIgnoreCase)).Value;
        return string.IsNullOrWhiteSpace(variable) ? null : variable.Trim();
    }

    private static IEnumerable<string> SplitProfiles(string? profile)
    {
        return profile is null
            ? []
            : profile.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // profile: the profile the file belongs to, which makes it required.
    private static void AddJsonFile(
        IConfigurationBuilder builder,
        IFileSystem fileSystem,
        string directory,
        string fileName,
        string? profile,
        List<string> sources,
        List<string> errors)
    {
        var path = fileSystem.Path.Combine(directory, fileName);
        AddJson(builder, fileSystem, path, fileName, profile is null ? null : $"The profile '{profile}' has no {fileName} in {directory}.", sources, errors);
    }

    // missingError: null when the file is optional.
    private static void AddJson(
        IConfigurationBuilder builder,
        IFileSystem fileSystem,
        string path,
        string label,
        string? missingError,
        List<string> sources,
        List<string> errors)
    {
        if (!fileSystem.File.Exists(path))
        {
            if (missingError is not null)
            {
                errors.Add(missingError);
            }

            return;
        }

        string text;
        try
        {
            text = fileSystem.File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{label} could not be read: {exception.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(text, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{label} must hold a JSON object.");
                return;
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"{label} is not valid JSON: {exception.Message}");
            return;
        }

        builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        sources.Add(label);
    }

    private static void AddUserSecrets(IConfigurationBuilder builder, IFileSystem fileSystem, UiAutomationSettingsOptions options, List<string> sources, List<string> errors)
    {
        var id = options.UserSecretsId ?? options.UserSecretsAssembly?.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        string path;
        try
        {
            path = PathHelper.GetSecretsPathFromSecretsId(id);
        }
        catch (InvalidOperationException)
        {
            // No user profile directory (APPDATA / HOME), e.g. in some containers: there are no user secrets.
            return;
        }
        catch (ArgumentException exception)
        {
            errors.Add($"The user secrets id '{id}' is not valid: {exception.Message}");
            return;
        }

        AddJson(builder, fileSystem, path, "user secrets", null, sources, errors);
    }

    private static void AddEnvironment(IConfigurationBuilder builder, IReadOnlyDictionary<string, string?> environment, List<string> sources)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in environment)
        {
            if (name.Length > EnvironmentPrefix.Length
                && name.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, ProfileVariable, StringComparison.OrdinalIgnoreCase))
            {
                values[name[EnvironmentPrefix.Length..].Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal)] = value;
            }
        }

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
            sources.Add($"{EnvironmentPrefix}* variables");
        }
    }

    private static Dictionary<string, string?> NormalizeParameters(IEnumerable<KeyValuePair<string, string?>>? parameters)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters ?? [])
        {
            var name = key.Trim().Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
            if (name.Length > 0)
            {
                values[name] = value;
            }
        }

        return values;
    }

    private static Dictionary<string, string?> ReadProcessEnvironment()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            values[(string)entry.Key] = entry.Value as string;
        }

        return values;
    }

    // The pool of the highest layer that sets an Email, with the passwords of every layer; null when no layer sets one.
    private static IConfiguration? ResolvePool(IConfigurationRoot configuration)
    {
        const string poolPath = "Users:Pool";
        var providers = configuration.Providers.ToList();
        var winner = -1;
        for (var index = providers.Count - 1; index >= 0 && winner < 0; index--)
        {
            if (ChildKeys(providers[index], poolPath).Any(entry => providers[index].TryGet($"{poolPath}:{entry}:Email", out _)))
            {
                winner = index;
            }
        }

        if (winner < 0)
        {
            return null;
        }

        var entries = ChildKeys(providers[winner], poolPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < providers.Count; index++)
        {
            foreach (var entry in entries)
            {
                foreach (var key in Leaves(providers[index], $"{poolPath}:{entry}"))
                {
                    var isPassword = string.Equals(key, $"{poolPath}:{entry}:Password", StringComparison.OrdinalIgnoreCase);
                    if ((index >= winner || isPassword) && providers[index].TryGet(key, out var value))
                    {
                        values[key] = value;
                    }
                }
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IEnumerable<string> ChildKeys(IConfigurationProvider provider, string path)
    {
        return provider.GetChildKeys([], path).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Leaves(IConfigurationProvider provider, string path)
    {
        if (provider.TryGet(path, out _))
        {
            yield return path;
        }

        foreach (var child in ChildKeys(provider, path))
        {
            foreach (var leaf in Leaves(provider, $"{path}:{child}"))
            {
                yield return leaf;
            }
        }
    }

    private static T BindSection<T>(IConfiguration configuration, string section, List<(string Section, string Error)> errors)
        where T : new()
    {
        var value = new T();
        try
        {
            configuration.GetSection(section).Bind(value);
        }
        catch (InvalidOperationException exception)
        {
            // "Failed to convert configuration value at 'Browser:Engine' to type …", plus the parser's reason.
            var reason = exception.InnerException is null ? exception.Message : $"{exception.Message} {exception.InnerException.Message}";
            errors.Add((section, reason));
        }

        return value;
    }
}
