using System;
using _42.Platform.Storyteller.Entities.Configurations;

namespace _42.Platform.Storyteller;

internal static class ConfigurationMappings
{
    public static ConfigurationVersion ToConfigurationVersion(this ConfigurationEntity e) => new()
    {
        Version = (uint)e.Version,
        Modified = e.Modified,
        ExpirationTime = DateTimeOffset.MaxValue,
        RestoredFrom = (uint?)e.RestoredFrom,
    };

    public static ConfigurationVersion ToConfigurationVersion(this ConfigurationHistoryEntity e) => new()
    {
        Version = (uint)e.Version,
        Modified = e.Modified,
        ExpirationTime = e.GetExpirationTime(),
        RestoredFrom = (uint?)e.RestoredFrom,
    };
}
