using Microsoft.Extensions.DependencyInjection;

namespace _42.nHolistic;

public interface ITestRunScopeFactory
{
    IServiceScope CreateScope();
}
