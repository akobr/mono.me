using MediatR;

namespace _42.tHolistic.Runner.VisualStudio;

public class TestCaseNotificationForRunnerHandler(ITestCasesRegister testCaseRegister) : INotificationHandler<TestCaseDiscoveredNotification>
{
    public Task Handle(TestCaseDiscoveredNotification notification, CancellationToken cancellationToken)
    {
        testCaseRegister.AddTestCase(notification.TestCase);
        return Task.CompletedTask;
    }
}
