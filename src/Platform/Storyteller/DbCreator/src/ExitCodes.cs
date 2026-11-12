using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator;

public static class ExitCodes
{
    public const int SUCCESS = 0;

    // input errors negative
    public const int ERROR_INPUT_PARSING = -1;

    // errors 1 - 39
    public const int ERROR_CRASH = 1;

    // wrong input: an unknown set, an invalid manifest or annotations.jsonl, a cycle, a bad name, an organization the
    // owner does not own, an invalid account id (DataSetException)
    public const int ERROR_WRONG_INPUT = 2;

    // the database does not have the layout of the shared container organizations, see DatabaseLayoutException
    public const int ERROR_DATABASE_LAYOUT = 3;

    // a document or an annotation of a data set failed, or a set was not seeded because a set it depends on failed
    public const int ERROR_SEED_FAILED = 4;

    // warnings from 40
    public const int WARNING_ABORTED = 40;
    public const int WARNING_NO_WORK_NEEDED = 42;
    public const int WARNING_INTERACTION_NEEDED = 43;
    public const int WARNING_UNAUTHORIZED_ACCESS = 44;

    // The code of a seed that ran: failures decide it, warnings never do, and a set seeded already is a success.
    public static int Of(DataSetRunResult run)
    {
        return run.HasFailures ? ERROR_SEED_FAILED : SUCCESS;
    }
}
