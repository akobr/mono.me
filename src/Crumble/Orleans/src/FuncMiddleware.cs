namespace _42.Crumble;

public class FuncMiddleware(Func<ICrumbInnerExecutionContext, Task> func) : IMiddleware
{
    IMiddleware? _next;

    public IMiddleware SetNext(IMiddleware middleware)
    {
        _next = middleware;
        return this;
    }

    public async Task Process(ICrumbInnerExecutionContext context)
    {
        await func(context);

        if (_next is not null)
        {
            await _next.Process(context);
        }
    }
}
