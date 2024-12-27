namespace _42.nHolistic;

public class TestRunContextProvider : ITestRunContextProvider
{
    public ITestRunContext GetContext()
    {
        return new TestRunContext();
    }
}
