using System.Text;

namespace _42.Platform.Storyteller.ConfigurationFormats;

public static class ConfigurationFormatNames
{
    private static readonly ConfigurationFormat[] Formats =
        [ConfigurationFormat.Flat, ConfigurationFormat.Properties, ConfigurationFormat.Env, ConfigurationFormat.Yaml];

    // flat, properties, env, yaml
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(Formats.Select(GetName).ToArray());

    // Exact names in any ASCII casing; no trimming and no numbers, unlike Enum.TryParse.
    public static bool TryParse(string? value, out ConfigurationFormat format)
    {
        foreach (var candidate in Formats)
        {
            if (value is not null && Ascii.EqualsIgnoreCase(value, candidate.GetName()))
            {
                format = candidate;
                return true;
            }
        }

        format = default;
        return false;
    }

    // The canonical lower-case name.
    public static string GetName(this ConfigurationFormat format)
    {
        return format switch
        {
            ConfigurationFormat.Flat => "flat",
            ConfigurationFormat.Properties => "properties",
            ConfigurationFormat.Env => "env",
            ConfigurationFormat.Yaml => "yaml",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a configuration format."),
        };
    }

    // The one source of the Content-Type of a formatted body.
    public static string GetMediaType(this ConfigurationFormat format)
    {
        return format switch
        {
            ConfigurationFormat.Flat => "application/json; charset=utf-8",
            ConfigurationFormat.Properties or ConfigurationFormat.Env => "text/plain; charset=utf-8",
            ConfigurationFormat.Yaml => "application/yaml; charset=utf-8",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a configuration format."),
        };
    }
}
