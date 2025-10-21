namespace _42.Crumble;

public interface ICrumbExecutionContextProvider
{
    public string? ContextKey { get; }

    public ICrumbExecutionContext GetExecutionContext();
}
