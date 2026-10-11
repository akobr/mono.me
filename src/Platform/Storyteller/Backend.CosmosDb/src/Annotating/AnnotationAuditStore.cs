using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Annotations;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Annotating;

// The audit document of an annotation: one item per key, appended in the same batch as the annotation write and
// kept within a size limit by dropping the oldest entries. The size is the length of the serialized item.
public sealed class AnnotationAuditStore
{
    // Room for the system properties Cosmos adds to the stored item (_rid, _self, _etag, _attachments, _ts).
    public const int SystemPropertiesAllowance = 200;

    private const int MaxAttempts = 3;

    private readonly AnnotationItemSerializer _serializer;
    private readonly AnnotationAuditOptions _options;
    private readonly TimeProvider _timeProvider;

    public AnnotationAuditStore(
        AnnotationItemSerializer serializer,
        IOptions<AnnotationAuditOptions> options,
        TimeProvider timeProvider)
    {
        _serializer = serializer;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public int MaxValueLength => _options.MaxValueLength;

    public static AnnotationAuditEntry NewEntry(AnnotationAuditAction action, AuditActor actor, DateTimeOffset at)
    {
        return new AnnotationAuditEntry
        {
            Id = Guid.CreateVersion7(at).ToString("N"),
            At = at,
            Action = action,
            Actor = actor,
        };
    }

    // The audit with the entries appended, trimmed to the size limit, and serialized. The stream goes to a batch.
    internal MemoryStream Prepare(
        AnnotationNode node,
        IEnumerable<AnnotationAuditEntry> entries,
        int? timeToLiveSeconds,
        out AnnotationAuditEntity audit)
    {
        var existing = node.Audit;
        audit = new AnnotationAuditEntity
        {
            PartitionKey = node.PartitionKey,
            Id = node.AuditId,
            ProjectName = node.Key.ProjectName,
            ViewName = node.Key.ViewName,
            AnnotationKey = node.KeyText,
            Name = node.Key.Annotation.Name,
            AuditedType = node.Key.Annotation.Type,
            Entries = (existing?.Entries ?? []).Concat(entries).ToList(),
            IsTrimmed = existing?.IsTrimmed ?? false,
            TimeToLiveInSeconds = timeToLiveSeconds,
        };

        var bytes = Fit(ref audit);
        return new MemoryStream(bytes, writable: false);
    }

    // Adds the prepared audit to a batch: a replace guarded by the read ETag, or a create when there was none.
    internal static Action<TransactionalBatch> Write(AnnotationNode node, MemoryStream stream)
    {
        var etag = node.AuditETag;
        var id = node.AuditId;

        return node.Audit is null
            ? batch => batch.CreateItemStream(stream)
            : batch => batch.ReplaceItemStream(id, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
    }

    // Appends one entry outside of an annotation write (configuration events), retrying concurrent appends.
    internal async Task AppendAsync(Container container, FullKey key, AnnotationAuditEntry entry)
    {
        var store = new AnnotationItemStore(container, _serializer);

        for (var attempt = 1; ; attempt++)
        {
            var node = await store.LoadAsync(key, withConfiguration: false);
            using var stream = Prepare(node, [entry], node.Audit?.TimeToLiveInSeconds, out _);

            var partitionKey = new PartitionKey(node.PartitionKey);
            using var response = node.Audit is null
                ? await container.CreateItemStreamAsync(stream, partitionKey)
                : await container.ReplaceItemStreamAsync(stream, node.AuditId, partitionKey, new ItemRequestOptions { IfMatchEtag = node.AuditETag });

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (attempt >= MaxAttempts
                || response.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed))
            {
                throw new CosmosException(
                    $"The audit of '{key.Annotation}' could not be written: {response.ErrorMessage}",
                    response.StatusCode,
                    0,
                    response.Headers.ActivityId,
                    response.Headers.RequestCharge);
            }
        }
    }

    internal DateTimeOffset GetNow() => _timeProvider.GetUtcNow();

    // Serializes the audit; above the limit, drops the oldest entries (the newest always stays) and serializes again.
    // The stored SizeInBytes is the length of the final serialization.
    private byte[] Fit(ref AnnotationAuditEntity audit)
    {
        var limit = Math.Clamp(_options.MaxDocumentBytes, 1024, AnnotationAuditOptions.MaxAllowedDocumentBytes) - SystemPropertiesAllowance;
        var bytes = _serializer.WriteAudit(audit with { SizeInBytes = 0 });

        if (bytes.Length > limit && audit.Entries.Count > 1)
        {
            var excess = bytes.Length - limit;
            var removed = 0;
            var dropped = 0;

            // one byte of each entry is its separating comma
            while (removed < excess && dropped < audit.Entries.Count - 1)
            {
                removed += _serializer.MeasureAuditEntry(audit.Entries[dropped]) + 1;
                dropped++;
            }

            audit = audit with { Entries = audit.Entries.Skip(dropped).ToList(), IsTrimmed = true };
        }

        // the size is part of the item: settle it in a few rounds (only its digits can change the length)
        for (var round = 0; round < 3; round++)
        {
            bytes = _serializer.WriteAudit(audit);

            if (bytes.Length == audit.SizeInBytes)
            {
                break;
            }

            audit = audit with { SizeInBytes = bytes.Length };
        }

        return _serializer.WriteAudit(audit);
    }
}
