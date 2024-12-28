using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace tHolistic.Runner.VisualStudio.Tests;

public class DebugTestCaseDiscoverySink : ITestCaseDiscoverySink
{
    public void SendTestCase(TestCase discoveredTest)
    {
        Debug.WriteLine($"[T {DateTime.Now:hh:mm:ss.ff}] {discoveredTest.FullyQualifiedName} {discoveredTest.DisplayName} Traits:{discoveredTest.Traits.Count()}:{string.Join(":", discoveredTest.Traits.Select(trait => trait.Name).ToHashSet())}");
    }
}
