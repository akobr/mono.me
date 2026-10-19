namespace _42.Platform.Cli.Configuration;

public class TelemetryOptions
{
    // The PostHog project token (phc_…), a write-only key meant for client applications. Empty turns telemetry off.
    public string? PostHogProjectToken { get; set; }

    // An absolute https URL, e.g. https://eu.i.posthog.com. Anything else turns telemetry off.
    public string? PostHogHost { get; set; }
}
