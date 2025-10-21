namespace _42.Crumble;

public class CrumbTracerFactory : ICrumbTracerFactory
{
    public ICrumbTracer CreateLogger()
    {
        return new DebugCrumbTracer();
    }
}
