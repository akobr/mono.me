using System.Buffers;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.Rendering;

// A .env file: DB__HOSTS__0=a, one line per value, in the dotenv dialect of the key/value export, extended so that
// common readers take every value literally.
internal static class EnvRenderer
{
    // Safe without quotes in every reader, also in those that take values verbatim.
    private static readonly SearchValues<char> BareCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.,:/@%+-");

    private static readonly SearchValues<char> NameCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-");

    // The prefix plays no part: a document is valid or not whatever the prefix is.
    public static void Validate(JToken? content)
    {
        PropertyNames.Check(content, (name, path, isRoot) =>
        {
            if (!IsValidName(name, isRoot))
            {
                throw new InvalidDocumentException(
                    DocumentRules.InvalidEnvName,
                    path,
                    $"The name of the property at {path} cannot be part of an environment variable name: use ASCII letters, digits, '.' and '-' with single '_' between them, and start the names of the root with a letter.");
            }
        });
    }

    // The segments upper-cased and joined with __, after the prefix: APP_DB__HOSTS__0.
    public static IReadOnlyList<EnvironmentVariable> ToVariables(IReadOnlyList<DocumentLeaf> leaves, string? prefix)
    {
        var start = prefix?.ToUpperInvariant() ?? string.Empty;

        return leaves
            .Select(leaf => new EnvironmentVariable(
                start + string.Join("__", leaf.Segments.Select(segment => (segment.Name ?? segment.Index.ToString(CultureInfo.InvariantCulture)).ToUpperInvariant())),
                leaf.Value,
                leaf.Kind,
                leaf.Path))
            .ToArray();
    }

    public static string Render(IReadOnlyList<EnvironmentVariable> variables)
    {
        var builder = new StringBuilder();

        foreach (var variable in variables)
        {
            builder.Append(variable.Name).Append('=').Append(Quote(variable.Value)).Append('\n');
        }

        return builder.ToString();
    }

    // [A-Za-z0-9.-]+(_[A-Za-z0-9.-]+)*: a single _ only between other characters, so __ stays the only delimiter and
    // a_:b cannot meet a:_b. Kubernetes' charset, with . and - kept for names such as Microsoft.AspNetCore.
    private static bool IsValidName(string name, bool isRoot)
    {
        if (name.Length == 0
            || (isRoot && !char.IsAsciiLetter(name[0]))
            || name[0] == '_'
            || name[^1] == '_'
            || name.AsSpan().ContainsAnyExcept(NameCharacters)
            || name.Contains("__", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    // Unquoted when every character is bare, so an empty value is NAME=; single-quoted, which bash, Docker Compose,
    // python-dotenv and the key/value import read literally, when nothing in the value needs an escape; otherwise
    // double-quoted with the escapes of the key/value export, in its order. $ is never escaped: the text is a dotenv
    // file, not a shell script.
    private static string Quote(string value)
    {
        if (!value.AsSpan().ContainsAnyExcept(BareCharacters))
        {
            return value;
        }

        if (value.AsSpan().IndexOfAny("'\\\r\n") < 0)
        {
            return $"'{value}'";
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }
}
