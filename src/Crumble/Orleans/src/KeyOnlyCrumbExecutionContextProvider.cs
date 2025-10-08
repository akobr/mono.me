namespace _42.Crumble;

public class KeyOnlyCrumbExecutionContextProvider(string contextKey) : ICrumbExecutionContextProvider
{
    private CrumbExecutionContext? _context;

    public string? ContextKey => contextKey;

    public ICrumbExecutionContext GetExecutionContext()
    {
        return _context ??= new CrumbExecutionContext()
        {
            ContextKey = contextKey,
        };
    }
}
