using System.Text;
using _42.Platform.Storyteller.ConfigurationFormats.Rendering;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats;

// Renders a configuration in a flat format. Every format reads the same canonical document: the base rules of
// DocumentFlattener first, then the name rule of the format over every name, then its value rule; the first error
// throws an InvalidDocumentException, and the text is complete or not returned at all. The output is deterministic,
// with LF line ends and keys in document order.
public static class ConfigurationFormatter
{
    // Bumped whenever the bytes of any format change, so that a tag of a formatted read changes with them.
    public const string Version = "1";

    // Replaces a surrogate it cannot encode with U+FFFD.
    private static readonly UTF8Encoding Utf8 = new(false);

    // environmentPrefix: for Env only, see ToEnvironmentVariables.
    public static FormattedConfiguration Format(JToken? content, ConfigurationFormat format, string? environmentPrefix = null)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Not a configuration format.");
        }

        if (environmentPrefix is not null && format != ConfigurationFormat.Env)
        {
            throw new ArgumentException("An environment prefix applies to the env format only.", nameof(environmentPrefix));
        }

        CheckPrefix(environmentPrefix);

        var canonical = Canonicalize(content);
        var leaves = DocumentFlattener.Read(canonical);

        var text = format switch
        {
            ConfigurationFormat.Flat => FlatRenderer.Render(leaves),
            ConfigurationFormat.Properties => RenderProperties(canonical, leaves),
            ConfigurationFormat.Env => RenderEnv(canonical, leaves, environmentPrefix),
            ConfigurationFormat.Yaml => YamlRenderer.Render(leaves),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a configuration format."),
        };

        return new FormattedConfiguration(format, text, format.GetMediaType());
    }

    // The env names with the prefix, and the raw values, kinds and paths, without quoting. The prefix must match
    // [A-Za-z][A-Za-z0-9_]*; it is upper-cased and prepended without a separator (APP_ gives APP_DB__HOST), and plays
    // no part in the rules.
    public static IReadOnlyList<EnvironmentVariable> ToEnvironmentVariables(JToken? content, string? environmentPrefix = null)
    {
        CheckPrefix(environmentPrefix);

        var canonical = Canonicalize(content);
        var leaves = DocumentFlattener.Read(canonical);
        EnvRenderer.Validate(canonical);
        return EnvRenderer.ToVariables(leaves, environmentPrefix);
    }

    // The document as a client reads it from the envelope: written as the API writes Content, and read back with the
    // settings of the SDK, so a date token is the ISO string Newtonsoft wrote and a decimal a double (1.50 is 1.5). An
    // unpaired surrogate becomes U+FFFD, as UTF-8 encoding makes it anyway and as YAML readers require. A token the
    // server parsed and one a client parsed then format to the same bytes.
    public static JToken? Canonicalize(JToken? content)
    {
        if (content is null)
        {
            return null;
        }

        var text = content.ToString(Formatting.None);

        if (text.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF'))
        {
            text = Utf8.GetString(Utf8.GetBytes(text));
        }

        // No depth limit: the token is already in memory, and the reader's default of 64 levels is not a rule of the
        // formats.
        using var reader = new JsonTextReader(new StringReader(text))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            MaxDepth = null,
        };

        return JToken.ReadFrom(reader);
    }

    private static string RenderProperties(JToken? canonical, IReadOnlyList<DocumentLeaf> leaves)
    {
        PropertiesRenderer.Validate(canonical);
        return PropertiesRenderer.Render(leaves);
    }

    private static string RenderEnv(JToken? canonical, IReadOnlyList<DocumentLeaf> leaves, string? environmentPrefix)
    {
        EnvRenderer.Validate(canonical);
        return EnvRenderer.Render(EnvRenderer.ToVariables(leaves, environmentPrefix));
    }

    private static void CheckPrefix(string? environmentPrefix)
    {
        if (environmentPrefix is null)
        {
            return;
        }

        if (environmentPrefix.Length == 0
            || !char.IsAsciiLetter(environmentPrefix[0])
            || environmentPrefix.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException(
                "An environment prefix starts with an ASCII letter and holds only ASCII letters, digits and '_'.",
                nameof(environmentPrefix));
        }
    }
}
