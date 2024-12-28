namespace _42.tHolistic;

public interface ITestExecutor
{
    Task ExecuteTestCasesAsync(IEnumerable<TestCase> testCases, CancellationToken cancellationToken = default);
}
