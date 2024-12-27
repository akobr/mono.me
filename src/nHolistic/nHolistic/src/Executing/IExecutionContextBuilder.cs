namespace _42.nHolistic;

public interface IExecutionContextBuilder
{
    public void RegisterTestCase(TestCaseContext testCase);

    public Task<ExecutionContext> BuildAsync();
}
