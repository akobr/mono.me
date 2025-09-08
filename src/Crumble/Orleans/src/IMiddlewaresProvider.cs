namespace _42.Crumble;

public interface IMiddlewaresProvider
{
    IMiddleware GetMiddlewareFullChain(Func<ICrumbInnerExecutionContext, Task> lastMiddleware);
}
