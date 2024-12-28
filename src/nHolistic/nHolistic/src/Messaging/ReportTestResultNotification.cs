using MediatR;

namespace _42.tHolistic;

public class ReportTestResultNotification : INotification
{
    public required TestRunResult Result { get; init; }
}
