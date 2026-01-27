namespace _42.Crumble;

public interface IJournalClient
{
    Task EnrollBeginningOfCrumb(ICrumbInnerExecutionContext context);

    Task EnrollEndOfCrumb(ICrumbInnerExecutionContext context);
}
