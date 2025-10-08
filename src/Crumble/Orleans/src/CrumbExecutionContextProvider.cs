namespace _42.Crumble;

public static class CrumbExecutionContextProvider
{
    public static ICrumbExecutionContextProvider FromContextKey(string contextKey)
    {
        return new KeyOnlyCrumbExecutionContextProvider(contextKey);
    }

    public static ICrumbExecutionContextProvider FromContext(ICrumbExecutionContext context)
    {
        return new SimpleCrumbExecutionContextProvider(context);
    }
}
