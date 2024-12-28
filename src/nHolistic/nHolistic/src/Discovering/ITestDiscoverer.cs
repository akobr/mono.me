using System.Reflection;

namespace _42.tHolistic;

public interface ITestDiscoverer
{
    public void DiscoverTests(Assembly assembly, string sourceName);
}
