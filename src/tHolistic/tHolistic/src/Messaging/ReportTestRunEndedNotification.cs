using MediatR;

namespace _42.tHolistic;

public class ReportTestRunEndedNotification : INotification
{
    public required TestCase TestCase { get; init; }

    public required TestRunOutcome TestOutcome { get; init; }
}
