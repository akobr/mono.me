using MediatR;

namespace _42.tHolistic.Runner.VisualStudio;

public class TestCasesProviderFromDiscoverer : ITestCasesProvider, ITestCasesRegister
{
    private readonly IVisualStudioTestDiscoverer _discoverer;
    private readonly IPublisher _publisher;
    private readonly List<TestCase> _testCases = new();

    public TestCasesProviderFromDiscoverer(
        IVisualStudioTestDiscoverer discoverer,
        IPublisher publisher)
    {
        _discoverer = discoverer;
        _discoverer.IsSecondaryService = true;
        _publisher = publisher;
    }

    public async Task<IEnumerable<TestCase>> GetTestCasesAsync()
    {
        if (_discoverer.DiscoveringProcess is null)
        {
            throw new InvalidOperationException("Discovering process is not started.");
        }

        await _publisher.Publish(
            new LogNotification { Message = "The execution process is waiting for test discovery to be finished." },
            CancellationToken.None);

        await _discoverer.DiscoveringProcess;
        return _testCases;
    }

    public void AddTestCase(TestCase testCase)
    {
        _testCases.Add(testCase);
    }
}
