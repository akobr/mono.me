using _42.tHolistic;
using Xunit;

namespace tHolistic.Tests;

public class ExecutionTests
{
    [Fact]
    public async Task BasicStepTest()
    {
        var stepContext = new StepSynchronizationContext(new StepTaskScheduler());
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(stepContext);
            await StepTaskFactory.Factory.StartNew(BasicStepsExample.Test1);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
