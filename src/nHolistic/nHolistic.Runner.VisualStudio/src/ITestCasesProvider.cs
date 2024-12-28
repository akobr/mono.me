namespace _42.tHolistic.Runner.VisualStudio;

public interface ITestCasesProvider
{
    Task<IEnumerable<TestCase>> GetTestCasesAsync();
}
