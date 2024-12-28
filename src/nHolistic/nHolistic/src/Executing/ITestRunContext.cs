namespace _42.tHolistic;

public interface ITestRunContext
{
    IDisposable StartStep(string stepName, StepAttribute step);
}
