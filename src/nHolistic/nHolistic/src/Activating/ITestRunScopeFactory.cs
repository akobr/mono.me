using Microsoft.Extensions.DependencyInjection;

namespace _42.tHolistic;

public interface ITestRunScopeFactory
{
    IServiceScope CreateScope();
}
