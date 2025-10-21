namespace _42.Crumble;

public class SimpleCrumbExecutionContextProvider(ICrumbExecutionContext context) : ICrumbExecutionContextProvider
{
    public string? ContextKey => context.ContextKey;

    public ICrumbExecutionContext GetExecutionContext()
    {
        return context;
    }
}
