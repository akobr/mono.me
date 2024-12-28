using MediatR;

namespace _42.tHolistic;

public class ReportAttachmentsNotification : INotification
{
    public required ICollection<string> Attachments { get; init; }
}
