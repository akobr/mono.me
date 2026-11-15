#nullable enable

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// What a test expects of a stored record; ShouldMatch checks the id, the time and the ttl on their own.
internal sealed record ExpectedAccessAudit(AccessAuditAction Action, string AccessPointKey, AccessCaller Caller)
{
    // Core "access", or the partition of the change in the container organizations.
    public string PartitionKey { get; init; } = AccessPointEntity.Partition;

    public AccessAuditTarget? Target { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public string? Detail { get; init; }
}
