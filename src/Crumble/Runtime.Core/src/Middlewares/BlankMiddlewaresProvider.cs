namespace _42.Crumble;

public class BlankMiddlewaresProvider : IMiddlewaresProvider
{
    public IMiddleware GetChainOfMiddlewares(Func<ICrumbInnerExecutionContext, Task> lastMiddleware)
    {
        return new FuncMiddleware(lastMiddleware);
    }
}
