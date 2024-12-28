using System.Diagnostics;

namespace _42.tHolistic;

public class TestRunContext : ITestRunContext
{
    public IDisposable StartStep(string stepName, StepAttribute step)
    {
        Debug.WriteLine($"Start of the step: {stepName}");
        return new StepScope(stepName);
    }

    private class StepScope(string name) : IDisposable
    {
        public void Dispose()
        {
            Debug.WriteLine($"End of the step: {name}");
        }
    }
}
