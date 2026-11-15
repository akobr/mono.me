using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Appearance = _42.Platform.Storyteller.ProjectTypeAppearance;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Services.Projects;

// What the console output uses of the settings: the appearances of the types, the date format and the version.
internal sealed record ProjectSettingsCacheValues
{
    [JsonPropertyName("version")]
    public long Version { get; init; }

    [JsonPropertyName("types")]
    public Dictionary<string, ProjectSettingsCacheAppearance>? Types { get; init; }

    [JsonPropertyName("dateFormat")]
    public string? DateFormat { get; init; }

    public static ProjectSettingsCacheValues From(SdkSettings settings)
    {
        return new ProjectSettingsCacheValues
        {
            Version = settings.Version,
            Types = (settings.Types ?? new Dictionary<string, SdkAppearance>())
                .Where(entry => entry.Value is not null)
                .ToDictionary(
                    entry => entry.Key,
                    entry => new ProjectSettingsCacheAppearance
                    {
                        Label = entry.Value.Label,
                        Plural = entry.Value.Plural,
                        Color = entry.Value.Color,
                        Icon = entry.Value.Icon,
                    },
                    StringComparer.Ordinal),
            DateFormat = settings.DateFormat,
        };
    }

    public IReadOnlyDictionary<string, Appearance> ToAppearances()
    {
        return (Types ?? [])
            .Where(entry => entry.Value is not null)
            .ToDictionary(
                entry => entry.Key,
                entry => new Appearance { Label = entry.Value.Label, Plural = entry.Value.Plural, Color = entry.Value.Color, Icon = entry.Value.Icon },
                StringComparer.Ordinal);
    }
}
