using System;

namespace _42.Platform.Storyteller.Entities;

public static class EntityExtensions
{
    public static DateTimeOffset GetLastUpdatedTime(this Entity @this)
    {
        return DateTimeOffset.FromUnixTimeSeconds(@this.LastUpdatedEpochTimestamp);
    }
}
