using System;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.Entities.KeyValues;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

// Builds the access audit records, with their time, id and ttl. Only the documents: every writer adds its record to the
// transactional batch of its change, and nothing updates or deletes one.
public sealed class AccessAuditRecords
{
    private readonly AccessAuditOptions _options;
    private readonly TimeProvider _timeProvider;

    public AccessAuditRecords(IOptions<AccessAuditOptions> options, TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // The default retention and the system clock, for the stores built without DI (tests, the base seeding).
    public static AccessAuditRecords Default { get; } = new(Options.Create(new AccessAuditOptions()));

    public int TimeToLiveInSeconds => (int)Math.Clamp(
        TimeSpan.FromDays(_options.RetentionDays).TotalSeconds,
        TimeSpan.FromDays(AccessAuditOptions.MinRetentionDays).TotalSeconds,
        TimeSpan.FromDays(AccessAuditOptions.MaxRetentionDays).TotalSeconds);

    // A record of core "access", the partition of the accounts and the access points.
    public AccessAuditEntity ForAccessPoint(string accessPointKey, AccessAuditChange change)
    {
        return Create(AccessPointEntity.Partition, accessPointKey, change);
    }

    // A record of the container organizations, in the partition of the change ("{project}.access", "{project}.federation").
    public AccessAuditEntity ForProject(string organization, string project, string partitionKey, AccessAuditChange change)
    {
        return Create(partitionKey, $"{organization}.{project}", change);
    }

    private AccessAuditEntity Create(string partitionKey, string accessPointKey, AccessAuditChange change)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessPointKey);
        ArgumentNullException.ThrowIfNull(change);

        var segments = accessPointKey.Split('.', 2);
        var now = _timeProvider.GetUtcNow();

        return new AccessAuditEntity
        {
            PartitionKey = partitionKey,
            Organization = segments[0],
            Id = AccessAuditEntity.NewId(now),
            TimeToLiveInSeconds = TimeToLiveInSeconds,
            Project = segments.Length == 2 ? segments[1] : null,
            AccessPointKey = accessPointKey,
            Time = KeyValueAuditEntity.FormatTime(now),
            Action = change.Action,
            Actor = change.Caller.Actor,
            Target = change.Target,
            OldValue = change.OldValue,
            NewValue = change.NewValue,
            Detail = change.Detail,
            CorrelationId = change.Caller.CorrelationId,
        };
    }
}
