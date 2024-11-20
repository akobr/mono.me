using System;

namespace _42.Platform.Storyteller.Entities.Configurations;

public static class ConfigurationHistoryEntityExtensions
{
    public static DateTimeOffset GetExpirationTime(this ConfigurationHistoryEntity @this)
    {
        var expirationEpochTimestamp = @this.LastUpdatedEpochTimestamp + @this.TimeToLiveInSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(expirationEpochTimestamp);
    }
}
