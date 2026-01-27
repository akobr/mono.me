namespace _42.Crumble;

// TODO: this will need a serializer
public record class MessageModel
{
    /// <summary>
    /// The Id of the Message.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// The content of the Message.
    /// </summary>
    public required string MessageText { get; init; }
}
