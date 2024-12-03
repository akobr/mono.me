namespace _42.nHolistic.Runner.VisualStudio;

public interface ITestCasesProvider
{
    Task<IEnumerable<TestCase>> GetTestCasesAsync();
}
