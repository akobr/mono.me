using MediatR;

namespace _42.nHolistic;

public class ReportAttachmentsNotification : INotification
{
    public required ICollection<string> Attachments { get; init; }
}
