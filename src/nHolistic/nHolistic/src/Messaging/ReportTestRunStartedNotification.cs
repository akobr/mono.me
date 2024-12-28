using MediatR;

namespace _42.tHolistic;

public class ReportTestRunStartedNotification : INotification
{
    public required TestCase TestCase { get; init; }
}
