using System.Reflection;

namespace _42.nHolistic;

public interface ITestDiscoverer
{
    public void DiscoverTests(Assembly assembly, string sourceName);
}
