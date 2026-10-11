namespace _42.Platform.Storyteller.Annotating;

// Cosmos DB counts an item's ttl from its last write (_ts), so every write to a deleted item recomputes the
// remaining seconds from the deletion. Everything one delete removed then expires together.
public static class DeletionRetention
{
    public static int GetRemainingSeconds(DateTimeOffset deletedAt, int retentionDays, DateTimeOffset now)
    {
        var remaining = deletedAt.AddDays(retentionDays) - now;
        var seconds = Math.Ceiling(remaining.TotalSeconds);

        return seconds switch
        {
            < 1 => 1,
            > int.MaxValue => int.MaxValue,
            _ => (int)seconds,
        };
    }

    public static DateTimeOffset? GetPurgeAt(long lastWriteEpochSeconds, int? timeToLiveSeconds)
    {
        return timeToLiveSeconds is > 0 && lastWriteEpochSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(lastWriteEpochSeconds + timeToLiveSeconds.Value)
            : null;
    }
}
