namespace _42.Crumble;

public class NullJournalClient : IJournalClient
{
    public Task EnrollBeginningOfCrumb(ICrumbInnerExecutionContext context)
    {
        return Task.CompletedTask;
    }

    public Task EnrollEndOfCrumb(ICrumbInnerExecutionContext context)
    {
        return Task.CompletedTask;
    }
}
