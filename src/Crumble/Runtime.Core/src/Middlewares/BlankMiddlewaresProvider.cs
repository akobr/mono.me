namespace _42.Crumble;

public class BlankMiddlewaresProvider : IMiddlewaresProvider
{
    public IMiddleware GetMiddlewareFullChain(Func<ICrumbInnerExecutionContext, Task> lastMiddleware)
    {
        return new FuncMiddleware(lastMiddleware);
    }
}
