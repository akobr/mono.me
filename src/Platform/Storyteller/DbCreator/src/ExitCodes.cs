namespace _42.Platform.Storyteller.DbCreator;

public static class ExitCodes
{
    public const int SUCCESS = 0;

    // input errors negative
    public const int ERROR_INPUT_PARSING = -1;

    // errors 1 - 39
    public const int ERROR_CRASH = 1;
    public const int ERROR_WRONG_INPUT = 2;

    // the database does not have the layout of the shared container organizations, see DatabaseLayoutException
    public const int ERROR_DATABASE_LAYOUT = 3;

    // warnings from 40
    public const int WARNING_ABORTED = 40;
    public const int WARNING_NO_WORK_NEEDED = 42;
    public const int WARNING_INTERACTION_NEEDED = 43;
    public const int WARNING_UNAUTHORIZED_ACCESS = 44;
}
