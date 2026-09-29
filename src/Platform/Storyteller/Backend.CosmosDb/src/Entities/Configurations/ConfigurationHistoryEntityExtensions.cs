using System;

namespace _42.Platform.Storyteller.Entities.Configurations;

public static class ConfigurationHistoryEntityExtensions
{
    public static DateTimeOffset GetExpirationTime(this ConfigurationHistoryEntity @this)
    {
        return GetExpirationTime(@this.LastUpdatedEpochTimestamp, @this.TimeToLiveInSeconds);
    }

    public static DateTimeOffset GetExpirationTime(this GenerateTemplateHistoryEntity @this)
    {
        return GetExpirationTime(@this.LastUpdatedEpochTimestamp, @this.TimeToLiveInSeconds);
    }

    private static DateTimeOffset GetExpirationTime(uint lastUpdatedEpochTimestamp, int timeToLiveInSeconds)
    {
        var expirationEpochTimestamp = lastUpdatedEpochTimestamp + timeToLiveInSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(expirationEpochTimestamp);
    }
}
