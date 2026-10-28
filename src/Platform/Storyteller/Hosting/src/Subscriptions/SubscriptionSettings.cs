using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;

namespace _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;

// StorytellerSubscriptionOptions after validation, with the scope the key and the names give. Immutable, read once at
// registration, so a bad key or option fails the registration, not the host's start.
internal sealed class SubscriptionSettings
{
    private const string OptionsName = nameof(StorytellerSubscriptionOptions);
    private const int MaxReads = 64;

    public required SubscriptionScope Scope { get; init; }

    // Zero polls once.
    public required TimeSpan PollInterval { get; init; }

    public required StorytellerReadMode Mode { get; init; }

    public required int MaxConcurrentReads { get; init; }

    public required bool WaitUntilReadyOnStart { get; init; }

    // Positive, or infinite.
    public required TimeSpan StartTimeout { get; init; }

    // responsibility: null takes the one in the chain of the key. The key is required either way: it gives the
    // organization, project and view, and a responsibility it names must be the one watched. Throws
    // StorytellerConfigurationException for the key and the options, ArgumentException for a name.
    public static SubscriptionSettings Create(StorytellerSettings settings, string? responsibility, string context, Action<StorytellerSubscriptionOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (responsibility is not null)
        {
            SubscriptionScope.ValidateName(responsibility, nameof(responsibility));
        }

        SubscriptionScope.ValidateName(context, nameof(context));

        var key = settings.Key ?? throw new StorytellerConfigurationException(
            $"The subscriptions watch in the organization, project and view of the key of the process: set {StorytellerVariables.KEY} or {nameof(StorytellerConfigurationOptions)}.{nameof(StorytellerConfigurationOptions.Key)} to a full key of the view, for example @{{organization}}.{{project}}.{{view}}.rst.{responsibility ?? "{responsibility}"}.");
        var scope = SubscriptionScope.Create(key, ResolveResponsibility(key, responsibility), context);

        var options = new StorytellerSubscriptionOptions();
        configure?.Invoke(options);
        var keyText = key.ToString();
        var mode = options.Mode ?? settings.Mode;

        if (!Enum.IsDefined(mode))
        {
            throw Invalid($"{OptionsName}.{nameof(options.Mode)} is not a read mode.", keyText);
        }

        // The rule of WatchExecutionsAsync, as both drive the same informer.
        if (!PollingSubscriptionSource.IsValidPollInterval(options.PollInterval))
        {
            throw Invalid($"{OptionsName}.{nameof(options.PollInterval)} must be {PollingSubscriptionSource.PollIntervalRule}.", keyText);
        }

        if (options.MaxConcurrentReads is < 1 or > MaxReads)
        {
            throw Invalid($"{OptionsName}.{nameof(options.MaxConcurrentReads)} must be 1 to {MaxReads}.", keyText);
        }

        if (options.StartTimeout <= TimeSpan.Zero && options.StartTimeout != Timeout.InfiniteTimeSpan)
        {
            throw Invalid($"{OptionsName}.{nameof(options.StartTimeout)} must be positive or infinite.", keyText);
        }

        return new SubscriptionSettings
        {
            Scope = scope,
            PollInterval = options.PollInterval,
            Mode = mode,
            MaxConcurrentReads = options.MaxConcurrentReads,
            WaitUntilReadyOnStart = options.WaitUntilReadyOnStart,
            StartTimeout = options.StartTimeout,
        };
    }

    // The responsibility in the chain of a key: its own (rst.R), or the one its unit (unt.R.U), usage (usg.S.R),
    // execution (exe.S.R.C) or unit of execution (uxe.S.R.C.U) names; empty for any other type.
    private static string ResolveResponsibility(FullKey key, string? responsibility)
    {
        var inKey = key.Annotation.ResponsibilityName;

        if (responsibility is null)
        {
            return inKey.Length > 0
                ? inKey
                : throw new StorytellerConfigurationException(
                    $"The key {key} names no responsibility to take the subscriptions of: set {StorytellerVariables.KEY} to a key of the responsibility or of one of its descendants (rst.{{responsibility}}, usg.…, exe.…, uxe.… or unt.…), or name the responsibility in AddSubscriptions(responsibility, context).",
                    key.ToString(),
                    null,
                    null,
                    false);
        }

        return inKey.Length == 0 || string.Equals(inKey, responsibility, StringComparison.Ordinal)
            ? responsibility
            : throw new StorytellerConfigurationException(
                $"AddSubscriptions names the responsibility {responsibility}, but the key {key} belongs to the responsibility {inKey}: a process watches the subscriptions of its own responsibility. Name {inKey}, or set {StorytellerVariables.KEY} to a key of {responsibility}.",
                key.ToString(),
                null,
                null,
                false);
    }

    private static StorytellerConfigurationException Invalid(string message, string key)
    {
        return new StorytellerConfigurationException(message, key, null, null, false);
    }
}
