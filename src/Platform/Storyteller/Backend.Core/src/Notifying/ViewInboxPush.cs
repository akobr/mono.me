namespace _42.Platform.Storyteller.Notifying;

// What a producer gives the inbox of a view.
public sealed record ViewInboxPush
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required ViewInboxItemKind Kind { get; init; }

    public required string Topic { get; init; }

    public ViewInboxItemLevel Level { get; init; }

    public required string Title { get; init; }

    public string? Text { get; init; }

    // No default: a producer that forgets it does not show its item to Contributors by accident.
    public required AccountRole MinimalRole { get; init; }

    public required AuditActor CreatedBy { get; init; }

    // Null: now plus the retention of the options.
    public DateTimeOffset? ExpiresAt { get; init; }

    // Of an Action: the code of its handler.
    public string? Action { get; init; }

    public ViewInboxJob? Job { get; init; }

    // Makes the push idempotent, with CreatedAt as the time of the event (required with a Key). The id is built from
    // both, so a second push is the same item only with the same Key AND the same CreatedAt: a producer passes the
    // time of its event, which it can read again, never the time of the push.
    public string? Key { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
