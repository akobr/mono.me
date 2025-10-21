namespace _42.Crumble;

public class OrleansCrumbExecutionContextProvider : ICrumbExecutionContextProvider
{
    private readonly ICrumbExecutionContext _context = OrleansCrumbExecutionContext.FromRequestContext();

    public string? ContextKey => _context.ContextKey;

    public ICrumbExecutionContext GetExecutionContext() => _context;
}
