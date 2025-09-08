namespace _42.Crumble;

public interface ICrumbExecutor : IAsyncDisposable, IDisposable
{
    ICrumbExecutionContext PrepareExecutionContext(Grain crumbGrain);

    TCrumbInstance CreateCrumbInstance<TCrumbInstance>();

    Task ExecuteCrumbWithMiddlewares(ICrumbInnerExecutionContext context, Func<Task> crumbAction);
}
