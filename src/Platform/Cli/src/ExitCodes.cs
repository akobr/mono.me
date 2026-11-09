namespace _42.Platform.Cli;

public static class ExitCodes
{
    public const int SUCCESS = 0;

    // input errors negative
    public const int ERROR_INPUT_PARSING = -1;

    // errors 1 - 39
    public const int ERROR_CRASH = 1;
    public const int ERROR_WRONG_INPUT = 2;

    // The API, the identity provider or a token endpoint failed or could not be reached; a retry may help. Which statuses
    // of the API it is: KeyValueConsoleExtensions.ExitCodeOf.
    public const int ERROR_SERVICE_FAILURE = 3;

    // warnings from 40
    public const int WARNING_ABORTED = 40;
    public const int WARNING_NO_WORK_NEEDED = 42;
    public const int WARNING_INTERACTION_NEEDED = 43;
    public const int WARNING_UNAUTHORIZED_ACCESS = 44;
    public const int WARNING_PARTIAL_RESULT = 45;
}
