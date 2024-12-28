namespace _42.tHolistic;

public interface IExecutionContextBuilder
{
    public void RegisterTestCase(TestCaseContext testCase);

    public Task<ExecutionContext> BuildAsync();
}
