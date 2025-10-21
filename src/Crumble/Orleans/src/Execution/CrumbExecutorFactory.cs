using Microsoft.Extensions.DependencyInjection;

namespace _42.Crumble;

public class CrumbExecutorFactory(IServiceProvider services) : ICrumbExecutorFactory
{
    public ICrumbExecutor CreateExecutor()
    {
        return new CrumbExecutor(services.CreateAsyncScope());
    }
}
