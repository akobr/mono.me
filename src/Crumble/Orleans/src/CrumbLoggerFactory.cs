namespace _42.Crumble;

public class CrumbLoggerFactory : ICrumbLoggerFactory
{
    public ICrumbLogger CreateLogger()
    {
        return new DebugCrumbLogger();
    }
}
