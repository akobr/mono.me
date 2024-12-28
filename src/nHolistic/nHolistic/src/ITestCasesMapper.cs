namespace _42.tHolistic;

public interface ITestCasesMapper
{
    int Count { get; }

    void RegisterTestCase(TestCaseContext testCase);

    IReadOnlyCollection<TestCaseContext> GetTestCases(string label);
}
