using System.Threading.Tasks;

namespace _42.Platform.Cli.Telemetry;

// Sends the one usage event of a run at the end of Main. Never throws and never changes the exit code.
internal interface ICommandTelemetry
{
    // The anonymous PostHog distinct_id; null when telemetry is off.
    string? InstallationId { get; }

    Task SendAsync(CommandRun run, int exitCode);
}
