using MediatR;

namespace _42.nHolistic;

public class LogNotification : INotification
{
    public LogMessageLevel Level { get; init; } = LogMessageLevel.Informational;

    public required string Message { get; init; }

    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
}
