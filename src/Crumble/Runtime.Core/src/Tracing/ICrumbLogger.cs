namespace _42.Crumble;

public interface ICrumbLogger : IDisposable
{
    void LogBeforeCrumbExecution(ICrumbInnerExecutionContext context);

    void LogAfterCrumbExecution(ICrumbInnerExecutionContext context);

    void LogBeforeMiddlewares(ICrumbInnerExecutionContext context);

    void LogAfterMiddlewares(ICrumbInnerExecutionContext context);

    void LogException(Exception exception, ICrumbInnerExecutionContext context);
}
