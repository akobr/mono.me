using System;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Model;

public static class ConfigurationVersionExtensions
{
    /// <summary>
    /// Whether the version is the current one; the current version never expires, history versions have a limited lifetime.
    /// </summary>
    public static bool IsCurrent(this ConfigurationVersion @this)
    {
        return @this.ExpirationTime.Year >= DateTimeOffset.MaxValue.Year;
    }
}
