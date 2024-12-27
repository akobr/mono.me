namespace _42.nHolistic;

public interface ITestRunContext
{
    IDisposable StartStep(string stepName, StepAttribute step);
}
