using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;

namespace _42.nHolistic.Runner.VisualStudio;

public class StaticTestCasesProvider : ITestCasesProvider
{
    private readonly IEnumerable<TestCase> _testCases;

    public StaticTestCasesProvider(IEnumerable<VsTestCase>? testCases)
    {
        _testCases = (testCases ?? []).Select(testCase => testCase.ToHolisticTestCase());
    }

    public Task<IEnumerable<TestCase>> GetTestCasesAsync()
    {
        return Task.FromResult(_testCases);
    }
}
