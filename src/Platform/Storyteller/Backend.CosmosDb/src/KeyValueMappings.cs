using System;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.KeyValues;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller;

internal static class KeyValueMappings
{
    public static KeyValue ToKeyValue(this KeyValueEntity e, string? value, DateTimeOffset now) => new()
    {
        Key = e.Key,
        Protection = e.Content.Protection,
        Value = value,
        ContentType = e.Content.ContentType,
        IsReference = KeyValueContentTypes.IsReference(e.Content.ContentType),
        Recipients = e.Content.Recipients,
        Version = e.Version,
        Created = e.Created,
        Modified = e.Modified,
        IsDeleted = e.IsDeleted,
        Deleted = e.Deleted,
        ExpiresAt = GetExpiration(e.LastUpdatedEpochTimestamp, e.TimeToLiveInSeconds, now),
    };

    public static KeyValue ToKeyValue(this KeyValueVersionEntity e, KeyValueEntity? catalog, string? value) => new()
    {
        Key = e.Key,
        Protection = e.Content.Protection,
        Value = value,
        ContentType = e.Content.ContentType,
        IsReference = KeyValueContentTypes.IsReference(e.Content.ContentType),
        Recipients = e.Content.Recipients,
        Version = e.Version,
        Created = catalog?.Created ?? e.Modified,
        Modified = e.Modified,
        IsDeleted = catalog?.IsDeleted ?? false,
        Deleted = catalog?.Deleted,
    };

    public static KeyValueVersionEntity ToVersion(this KeyValueEntity e, int timeToLiveInSeconds) => new()
    {
        PartitionKey = e.PartitionKey,
        Id = $"{EntityIdPrefixTypes.KeyValueVersion}.{KeyValueRules.Normalize(e.Key)}.{e.Version}",
        TimeToLiveInSeconds = timeToLiveInSeconds,
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        Key = e.Key,
        NormalizedKey = KeyValueRules.Normalize(e.Key),
        Version = e.Version,
        Content = e.Content,
        Modified = e.Modified,
    };

    public static KeyValueVersion ToCurrentVersion(this KeyValueEntity e, DateTimeOffset now) => new()
    {
        Version = e.Version,
        Protection = e.Content.Protection,
        Modified = e.Modified,
        ExpirationTime = GetExpiration(e.LastUpdatedEpochTimestamp, e.TimeToLiveInSeconds, now),
        IsCurrent = true,
    };

    public static KeyValueVersion ToVersion(this KeyValueVersionRow row, DateTimeOffset now) => new()
    {
        Version = row.Version,
        Protection = row.Protection,
        Modified = row.Modified,
        ExpirationTime = GetExpiration(row.Timestamp, row.TimeToLive, now),
    };

    public static KeyValueSummary ToSummary(this KeyValueListingRow row, DateTimeOffset now) => new()
    {
        Key = row.Key ?? row.Id[(EntityIdPrefixTypes.KeyValue.Length + 1)..],
        Protection = row.Protection,
        Value = row.Protection == KeyValueProtection.Public ? row.Value : null,
        ContentType = row.ContentType,
        IsReference = KeyValueContentTypes.IsReference(row.ContentType),
        Version = row.Version,
        Modified = row.Modified,
        IsDeleted = row.Deleted is not null,
        ExpiresAt = GetExpiration(row.Timestamp, row.TimeToLive, now),
    };

    // Cosmos counts an item's ttl from its last write (_ts); an item written in this request has no _ts yet.
    public static DateTimeOffset? GetExpiration(uint lastUpdatedEpochTimestamp, int? timeToLiveInSeconds, DateTimeOffset now)
    {
        if (timeToLiveInSeconds is null)
        {
            return null;
        }

        var written = lastUpdatedEpochTimestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(lastUpdatedEpochTimestamp)
            : now;

        return written.AddSeconds(timeToLiveInSeconds.Value);
    }
}
