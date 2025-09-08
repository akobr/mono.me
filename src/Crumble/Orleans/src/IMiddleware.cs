namespace _42.Crumble;

public interface IMiddleware
{
    IMiddleware SetNext(IMiddleware middleware);

    Task Process(ICrumbInnerExecutionContext context);
}
