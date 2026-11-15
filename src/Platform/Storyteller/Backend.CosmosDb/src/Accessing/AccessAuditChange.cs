namespace _42.Platform.Storyteller.Accessing;

// What an access change records: the action, the caller, and what it was made to.
public sealed record AccessAuditChange(AccessAuditAction Action, AccessCaller Caller)
{
    public AccessAuditTarget? Target { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public string? Detail { get; init; }
}
