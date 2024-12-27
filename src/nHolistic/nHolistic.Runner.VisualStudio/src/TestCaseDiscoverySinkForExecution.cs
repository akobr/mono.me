using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace _42.nHolistic.Runner.VisualStudio;

public class TestCaseDiscoverySinkForExecution : ITestCaseDiscoverySink
{
    public void SendTestCase(Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase discoveredTest)
    {
        throw new NotImplementedException();
    }
}
