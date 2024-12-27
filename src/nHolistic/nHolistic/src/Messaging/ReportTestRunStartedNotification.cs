using MediatR;

namespace _42.nHolistic;

public class ReportTestRunStartedNotification : INotification
{
    public required TestCase TestCase { get; init; }
}
