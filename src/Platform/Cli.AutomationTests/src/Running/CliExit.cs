namespace _42.Platform.Cli.AutomationTests.Running;

// The exit codes of sform, mirrored from src/Platform/Cli/src/ExitCodes.cs (the CLI keeps its internals internal, so the
// tests carry their own copy; SformResult.ShouldExitWith takes it). A crash (1) fails every case of the catalogue.
public enum CliExit
{
    // ERROR_INPUT_PARSING: an unknown command or option, a missing or invalid option value.
    InputParsing = -1,

    // SUCCESS.
    Success = 0,

    // ERROR_CRASH: an unhandled exception ("Total mayhem").
    Crash = 1,

    // ERROR_WRONG_INPUT: a wrong value, a missing value on redirected input, a not-found key.
    WrongInput = 2,

    // ERROR_SERVICE_FAILURE: the API or the identity provider failed, or could not be reached.
    ServiceFailure = 3,

    // WARNING_ABORTED: a prompt answered No.
    Aborted = 40,

    // WARNING_NO_WORK_NEEDED: nothing to change.
    NoWorkNeeded = 42,

    // WARNING_INTERACTION_NEEDED: a confirmation on redirected input, a command that needs a terminal, no default project.
    InteractionNeeded = 43,

    // WARNING_UNAUTHORIZED_ACCESS: the API refused the caller (401/403), a declined sign-in.
    Unauthorized = 44,

    // WARNING_PARTIAL_RESULT.
    PartialResult = 45,
}
