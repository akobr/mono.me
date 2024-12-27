using MediatR;

namespace _42.nHolistic;

public class ReportTestResultNotification : INotification
{
    public required TestRunResult Result { get; init; }
}
