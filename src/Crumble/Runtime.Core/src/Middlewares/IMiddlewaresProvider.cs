namespace _42.Crumble;

public interface IMiddlewaresProvider
{
    IMiddleware GetChainOfMiddlewares(Func<ICrumbInnerExecutionContext, Task> lastMiddleware);
}
