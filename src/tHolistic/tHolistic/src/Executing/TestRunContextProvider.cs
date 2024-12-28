namespace _42.tHolistic;

public class TestRunContextProvider : ITestRunContextProvider
{
    public ITestRunContext GetContext()
    {
        return new TestRunContext();
    }
}
