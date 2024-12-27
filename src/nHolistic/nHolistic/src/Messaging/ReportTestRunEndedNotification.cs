using MediatR;

namespace _42.nHolistic;

public class ReportTestRunEndedNotification : INotification
{
    public required TestCase TestCase { get; init; }

    public required TestRunOutcome TestOutcome { get; init; }
}
