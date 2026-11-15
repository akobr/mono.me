using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Services.Projects;

internal sealed record ProjectSettingsCacheAppearance
{
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("plural")]
    public string? Plural { get; init; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
}
