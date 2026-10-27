namespace _42.Platform.Cli.GitHubActions;

// Workflow commands of GitHub Actions, read by the runner from the standard output of a step.
internal static class WorkflowCommands
{
    // Hides the value in the rest of the job's log.
    public static string AddMask(string value)
    {
        return $"::add-mask::{EscapeData(value)}\n";
    }

    // The data of a command with '%', CR and LF escaped, as the runner reads it back.
    public static string EscapeData(string value)
    {
        return value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
    }
}
