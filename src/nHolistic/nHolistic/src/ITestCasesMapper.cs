namespace _42.nHolistic;

public interface ITestCasesMapper
{
    int Count { get; }

    void RegisterTestCase(TestCaseContext testCase);

    IReadOnlyCollection<TestCaseContext> GetTestCases(string label);
}
