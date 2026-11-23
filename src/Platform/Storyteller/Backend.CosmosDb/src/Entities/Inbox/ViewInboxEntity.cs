using System;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Notifying;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Inbox;

// An item of the inbox of a view: id "inb.{item id}" in the partition "{project}.inbox.{view}" of the organization
// container. It has no ViewName property on purpose, so view discovery never counts it as data of a view, and no
// AnnotationType, so annotation queries never read it. Nothing deletes it; it expires by ttl.
public sealed record class ViewInboxEntity
{
    public const string IdPrefix = "inb.";

    // The id order is the time order: inb.{yyyyMMddHHmmssfffffff}{32 hex}.
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string PartitionKey { get; init; }

    // The organization of the item: the first level of the hierarchical partition key of the container
    // organizations (/Organization, /PartitionKey). Cosmos DB refuses a write whose key does not match it.
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required string ItemId { get; init; }

    public required ViewInboxItemKind Kind { get; init; }

    public required string Topic { get; init; }

    public ViewInboxItemLevel Level { get; init; }

    public required string Title { get; init; }

    public string? Text { get; init; }

    // The list and the count filter on it and on the state.
    public required AccountRole MinimalRole { get; init; }

    public required ViewInboxItemState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required AuditActor CreatedBy { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public AuditStamp? Closed { get; init; }

    public string? Action { get; init; }

    public ViewInboxJob? Job { get; init; }

    // The time the item still has at the write. Cosmos DB counts it from the write, so every write gets what is left.
    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public required int TimeToLiveInSeconds { get; init; }

    public static string GetId(string itemId)
    {
        return $"{IdPrefix}{itemId}";
    }

    public static ViewInboxEntity FromRecord(ViewInboxRecord record, int timeToLiveInSeconds) => new()
    {
        Id = GetId(record.Item.Id),
        PartitionKey = PartitionKeys.GetInbox(record.Project, record.View),
        Organization = record.Organization,
        Project = record.Project,
        View = record.View,
        ItemId = record.Item.Id,
        Kind = record.Item.Kind,
        Topic = record.Item.Topic,
        Level = record.Item.Level,
        Title = record.Item.Title,
        Text = record.Item.Text,
        MinimalRole = record.Item.MinimalRole,
        State = record.Item.State,
        CreatedAt = record.Item.CreatedAt,
        CreatedBy = record.Item.CreatedBy,
        ExpiresAt = record.Item.ExpiresAt,
        Closed = record.Item.Closed,
        Action = record.Item.Action,
        Job = record.Item.Job,
        TimeToLiveInSeconds = timeToLiveInSeconds,
    };

    // The ETag of a point read or of a write; a query has none.
    public ViewInboxRecord ToRecord(string? etag = null) => new()
    {
        Organization = Organization,
        Project = Project,
        View = View,
        Item = new ViewInboxItem
        {
            Id = ItemId,
            Kind = Kind,
            Topic = Topic,
            Level = Level,
            Title = Title,
            Text = Text,
            MinimalRole = MinimalRole,
            State = State,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            ExpiresAt = ExpiresAt,
            Closed = Closed,
            Action = Action,
            Job = Job,
        },
        ETag = etag,
    };
}
