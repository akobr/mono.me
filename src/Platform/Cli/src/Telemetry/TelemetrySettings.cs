using System;
using System.Linq;
using _42.Platform.Cli.Configuration;

namespace _42.Platform.Cli.Telemetry;

// Whether sform sends its usage event, from the telemetry section and the opt-out variables, read once per process.
// Off with a blank token, a host that is not an absolute https URL (the SDK would fall back to its US cloud),
// or DO_NOT_TRACK / SFORM_TELEMETRY_OPTOUT set to 1, true, yes or on.
public sealed class TelemetrySettings
{
    public const string DO_NOT_TRACK = "DO_NOT_TRACK";
    public const string SFORM_TELEMETRY_OPTOUT = "SFORM_TELEMETRY_OPTOUT";

    private static readonly string[] OptOutVariables = [DO_NOT_TRACK, SFORM_TELEMETRY_OPTOUT];
    private static readonly string[] OptOutValues = ["1", "true", "yes", "on"];

    // Set by the common CI services; only their presence is reported, never a value.
    private static readonly string[] ContinuousIntegrationVariables = ["CI", "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "JENKINS_URL"];

    private TelemetrySettings()
    {
    }

    public bool IsEnabled { get; private init; }

    public string? ProjectToken { get; private init; }

    public Uri? Host { get; private init; }

    public bool IsContinuousIntegration { get; private init; }

    public static TelemetrySettings Resolve(TelemetryOptions options, Func<string, string?> getVariable)
    {
        string? Read(string name)
        {
            var value = getVariable(name)?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        var token = options.PostHogProjectToken?.Trim();
        var isOptedOut = OptOutVariables.Any(name => Read(name) is { } value && OptOutValues.Contains(value, StringComparer.OrdinalIgnoreCase));
        var isContinuousIntegration = ContinuousIntegrationVariables.Any(name => Read(name) is not null);

        if (string.IsNullOrEmpty(token)
            || !Uri.TryCreate(options.PostHogHost?.Trim(), UriKind.Absolute, out var host)
            || host.Scheme != Uri.UriSchemeHttps
            || isOptedOut)
        {
            return new TelemetrySettings { IsContinuousIntegration = isContinuousIntegration };
        }

        return new TelemetrySettings
        {
            IsEnabled = true,
            ProjectToken = token,
            Host = host,
            IsContinuousIntegration = isContinuousIntegration,
        };
    }
}
