using Microsoft.Extensions.Logging;

namespace _42.Crumble;

public class DefaultCrumbTracer(ILogger<ICrumbTracer> logger) : ICrumbTracer
{
    public void LogBeforeCrumbExecution(ICrumbInnerExecutionContext context)
    {
        // no operation
    }

    public void LogAfterCrumbExecution(ICrumbInnerExecutionContext context)
    {
        // no operation
    }

    public void LogBeforeMiddlewares(ICrumbInnerExecutionContext context)
    {
        // no operation
    }

    public void LogAfterMiddlewares(ICrumbInnerExecutionContext context)
    {
        // no operation
    }

    public void LogException(Exception exception, ICrumbInnerExecutionContext context)
    {
        logger.LogError(exception, "Exception in crumb {CrumbKey} with Id {Id} and context {ContextKey}", context.CrumbKey, context.ExecutionContext.Id, context.ExecutionContext.ContextKey);
    }

    public void Dispose()
    {
        // no operation
    }
}
