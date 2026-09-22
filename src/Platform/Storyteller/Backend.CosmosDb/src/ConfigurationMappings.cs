using System;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;

namespace _42.Platform.Storyteller;

internal static class ConfigurationMappings
{
    public static ConfigurationVersion ToConfigurationVersion(this ConfigurationEntity e) => new()
    {
        Version = (uint)e.Version,
        Author = e.Author,
        CreationTime = e.GetLastUpdatedTime(),
        ExpirationTime = DateTimeOffset.MaxValue,
    };

    public static ConfigurationVersion ToConfigurationVersion(this ConfigurationHistoryEntity e) => new()
    {
        Version = (uint)e.Version,
        Author = e.Author,
        CreationTime = e.CreationTime,
        ExpirationTime = e.GetExpirationTime(),
    };
}
