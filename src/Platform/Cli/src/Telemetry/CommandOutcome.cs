using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Telemetry;

// The outcome property of the usage event. The first matching rule wins: a validation failure of CommandLineUtils
// returns 1 like a crash, so a crash is told by where the exception was caught, not by the exit code.
internal static class CommandOutcome
{
    public const string CANCELLED = "cancelled";
    public const string HELP = "help";
    public const string VERSION = "version";
    public const string INPUT_ERROR = "input_error";
    public const string CRASH = "crash";
    public const string SUCCESS = "success";
    public const string WARNING = "warning";
    public const string ERROR = "error";

    public static string Of(CommandRun run, int exitCode)
    {
        if (run.Cancelled)
        {
            return CANCELLED;
        }

        if (run.Information is not null)
        {
            return run.Information;
        }

        if (run.Exception is CommandParsingException || exitCode < 0)
        {
            return INPUT_ERROR;
        }

        if (run.Crashed)
        {
            return CRASH;
        }

        if (exitCode == ExitCodes.SUCCESS)
        {
            return SUCCESS;
        }

        return exitCode >= ExitCodes.WARNING_ABORTED ? WARNING : ERROR;
    }
}
