using MediatR;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace _42.nHolistic.Runner.VisualStudio;

public class TestCaseNotificationHandler(ITestCaseDiscoverySink sink) : INotificationHandler<TestCaseDiscoveredNotification>
{
    public Task Handle(TestCaseDiscoveredNotification notification, CancellationToken cancellationToken)
    {
        sink.SendTestCase(notification.TestCase.ToVsTestCase());
        return Task.CompletedTask;
    }
}
