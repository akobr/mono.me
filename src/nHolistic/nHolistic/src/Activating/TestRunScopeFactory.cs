using Microsoft.Extensions.DependencyInjection;

namespace _42.nHolistic;

public class TestRunScopeFactory(IServiceProvider provider) : ITestRunScopeFactory
{
    public IServiceScope CreateScope()
    {
        return provider.CreateScope();
    }
}
