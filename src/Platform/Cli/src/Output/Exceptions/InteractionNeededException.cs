namespace _42.Platform.Cli.Output.Exceptions;

// A prompt that cannot be shown because the standard input is redirected. Its message is already on standard error
// (InteractiveInputExtensions); the exit code is WARNING_INTERACTION_NEEDED for a confirmation or a command that needs
// a terminal, and ERROR_WRONG_INPUT for a value that an option supplies.
public sealed class InteractionNeededException : OutputException
{
    public InteractionNeededException(int exitCode, string message)
        : base(exitCode, message)
    {
        // no operation
    }
}
