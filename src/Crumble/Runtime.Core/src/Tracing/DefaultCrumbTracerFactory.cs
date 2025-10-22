using Microsoft.Extensions.DependencyInjection;

namespace _42.Crumble;

public class DefaultCrumbTracerFactory(IServiceProvider services) : ICrumbTracerFactory
{
    public ICrumbTracer CreateLogger()
    {
        return ActivatorUtilities.CreateInstance<DefaultCrumbTracer>(services);
    }
}
