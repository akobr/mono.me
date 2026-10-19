using System.Threading.Tasks;

namespace _42.Platform.Cli.Telemetry;

// Telemetry is off: no PostHog client is built, so none of its "disabled" warnings appear either.
internal sealed class NoCommandTelemetry : ICommandTelemetry
{
    public static readonly NoCommandTelemetry Instance = new();

    private NoCommandTelemetry()
    {
    }

    public string? InstallationId => null;

    public Task SendAsync(CommandRun run, int exitCode)
    {
        return Task.CompletedTask;
    }
}
