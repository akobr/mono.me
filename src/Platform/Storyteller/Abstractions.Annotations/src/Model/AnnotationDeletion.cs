using System;

namespace _42.Platform.Storyteller;

// Present only on a soft-deleted annotation.
public record class AnnotationDeletion
{
    // Guid v7 ("N"), shared by everything one delete removed.
    public required string DeletionId { get; init; }

    // The key the delete was requested for.
    public required string RootKey { get; init; }

    public required DateTimeOffset DeletedAt { get; init; }

    public required AuditActor DeletedBy { get; init; }

    // Listed as a root of Deleted: the key a delete was requested for, or a deleted child whose parent became
    // active again while the child stayed deleted.
    public bool IsRoot { get; init; }

    // On the requested root only.
    public int? DescendantCount { get; init; }

    // When the automatic purge removes it: the item's last write plus its ttl, which is what Cosmos applies.
    // Computed on read, never stored.
    public DateTimeOffset? PurgeAt { get; init; }
}
