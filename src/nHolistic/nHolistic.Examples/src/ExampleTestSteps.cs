using System.Diagnostics;

namespace _42.nHolistic.Examples;

[Steps]
public class ExampleTestSteps
{
    [Step]
    public Task FirstStep(IExampleFirstStepModel model)
    {
        // do first step
        Debug.WriteLine($"First step: {model.Text}");
        return Task.CompletedTask;
    }

    [Step]
    public Task SecondStep(IExampleSecondStepModel model)
    {
        // do second step
        Debug.WriteLine($"Second step: {model.Number}");
        return Task.Delay(10000);
    }
}
