namespace _42.nHolistic;

public interface ITestExecutor
{
    Task ExecuteTestCasesAsync(IEnumerable<TestCase> testCases, CancellationToken cancellationToken = default);
}
