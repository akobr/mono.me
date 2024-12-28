using System.Diagnostics;
using _42.tHolistic;

namespace nHolistic.Tests;

public class BasicStepsExample
{
    [Test]
    public static async Task Test1()
    {
        Debug.WriteLine("Before first step");
        await FirstStep();
        Debug.WriteLine("Between steps");
        await SecondStep("finished");
        Debug.WriteLine("After second step");
    }

    [Step]
    public static async Task FirstStep()
    {
        Debug.WriteLine("First step done");
    }

    [Step]
    public static Task SecondStep(string input)
    {
        Debug.WriteLine($"Second step: {input}");
        return Task.Delay(2000);
    }

    private static Task MakeStep(Action step)
    {
        return StepTaskFactory.Factory.StartNew(step);
    }

    private static Task MakeStep(Func<Task> step)
    {
        return StepTaskFactory.Factory.StartNew(step);
    }
}
