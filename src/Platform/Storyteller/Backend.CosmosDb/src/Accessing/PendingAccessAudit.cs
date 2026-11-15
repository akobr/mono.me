using System;
using System.Threading;
using _42.Platform.Storyteller.Entities.Access;

namespace _42.Platform.Storyteller.Accessing;

// The access audit record of a change that a store further down commits, for that store to write in the batch of the
// change: an ambient record, like TransactionScope's transaction or KeyValueCallerContext's caller. Today only the reset
// of an API key: the access service opens it with the record of the reset, and CosmosMergedApiKeyHashStore patches the
// new hash in a batch with it, three layers down, behind interfaces that know nothing of Cosmos.
internal sealed class PendingAccessAudit
{
    private static readonly AsyncLocal<PendingAccessAudit?> Ambient = new();

    private PendingAccessAudit(string organization, string partitionKey, string itemId, AccessAuditEntity record)
    {
        Organization = organization;
        PartitionKey = partitionKey;
        ItemId = itemId;
        Record = record;
    }

    public string Organization { get; }

    public string PartitionKey { get; }

    public string ItemId { get; }

    public AccessAuditEntity Record { get; }

    public bool IsWritten { get; private set; }

    public static Scope Begin(string organization, string partitionKey, string itemId, AccessAuditEntity record)
    {
        return new Scope(new PendingAccessAudit(organization, partitionKey, itemId, record));
    }

    // The open record of a change to this item, while nothing wrote it.
    public static PendingAccessAudit? Find(string organization, string partitionKey, string itemId)
    {
        return Ambient.Value is { IsWritten: false } pending
            && string.Equals(pending.Organization, organization, StringComparison.Ordinal)
            && string.Equals(pending.PartitionKey, partitionKey, StringComparison.Ordinal)
            && string.Equals(pending.ItemId, itemId, StringComparison.Ordinal)
                ? pending
                : null;
    }

    // Called after the batch with the record succeeded.
    public void MarkWritten()
    {
        IsWritten = true;
    }

    public sealed class Scope : IDisposable
    {
        private readonly PendingAccessAudit? _previous;

        public Scope(PendingAccessAudit pending)
        {
            _previous = Ambient.Value;
            Pending = pending;
            Ambient.Value = pending;
        }

        public PendingAccessAudit Pending { get; }

        public void Dispose()
        {
            Ambient.Value = _previous;
        }
    }
}
