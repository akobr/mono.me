namespace _42.Platform.Storyteller.Notifying;

// A stored item: the public model and the view it belongs to.
public sealed record class ViewInboxRecord
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required ViewInboxItem Item { get; init; }

    // The concurrency token of the stored item; null before the first write and on the records of a query.
    public string? ETag { get; init; }
}
