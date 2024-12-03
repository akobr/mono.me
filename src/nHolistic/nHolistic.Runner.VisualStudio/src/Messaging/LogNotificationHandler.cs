using MediatR;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace _42.nHolistic.Runner.VisualStudio;

public class LogNotificationHandler(IMessageLogger logger) : INotificationHandler<LogNotification>
{
    private readonly DateTime _startTime = DateTime.UtcNow;

    public Task Handle(LogNotification notification, CancellationToken cancellationToken)
    {
        logger.SendMessage((TestMessageLevel)notification.Level, GetFormatedMessage(notification));
        return Task.CompletedTask;
    }

    private string GetFormatedMessage(LogNotification notification)
    {
        var timeDiff = notification.TimeStamp - _startTime;
        return $"[{Constants.RunnerName} {timeDiff:mm\\:ss\\.ff}] {notification.Message}";
    }
}
