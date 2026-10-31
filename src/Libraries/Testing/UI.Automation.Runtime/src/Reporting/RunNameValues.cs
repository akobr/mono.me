using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// What the placeholders of Reporting:RunName and Reporting:RunDescription (and BrowserStack:Build) stand for.
// StartedAt is local time, so {Date} and {Time} read as on the machine that ran.
public sealed record RunNameValues
{
    public required string RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public string? Profile { get; init; }

    public string? Provider { get; init; }

    public string? Engine { get; init; }

    public string? Device { get; init; }

    public string? Branch { get; init; }

    public string? Sha { get; init; }

    public string? BuildNumber { get; init; }

    public string? Machine { get; init; }

    // The values of a run of settings that started at localStartedAt (local time) in environment.
    public static RunNameValues For(UiAutomationSettings settings, string runId, DateTimeOffset localStartedAt, RunEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(environment);

        return new RunNameValues
        {
            RunId = runId,
            StartedAt = localStartedAt,
            Profile = settings.Profile,
            Provider = settings.Browser.Provider.ToString(),
            Engine = settings.EffectiveEngine.ToString().ToLowerInvariant(),
            Device = string.IsNullOrWhiteSpace(settings.Browser.Device) ? null : settings.Browser.Device,
            Branch = environment.Branch,
            Sha = environment.Sha,
            BuildNumber = environment.BuildNumber,
            Machine = environment.Machine,
        };
    }
}
