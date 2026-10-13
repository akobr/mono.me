using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.KeyValues;

public enum KeyValueFileFormat
{
    // The 2s.key-values.v1 document of the API: keys, levels, values and content types.
    Json = 0,

    // One JSON object of key → string value (nested objects are flattened to dotted keys).
    Flat,

    // .env lines: KEY=VALUE, "#" comments, an optional "export " prefix, quoted values with escapes; "." becomes "__".
    Env,
}

// The import and export formats of the CLI, following the conventions of the TypeScript SDK (lib/key-values.ts).
public static partial class KeyValueFormats
{
    public const string DocumentFormat = "2s.key-values.v1";

    public static bool TryParseFormat(string? text, out KeyValueFileFormat format)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "json":
            case "document":
                format = KeyValueFileFormat.Json;
                return true;
            case "flat":
            case "flat-json":
                format = KeyValueFileFormat.Flat;
                return true;
            case "env":
            case "dotenv":
            case ".env":
                format = KeyValueFileFormat.Env;
                return true;
            default:
                format = default;
                return false;
        }
    }

    // The format of a file by its name and, when the name does not tell, its content.
    public static KeyValueFileFormat Detect(string? path, string? content)
    {
        var name = path is null ? string.Empty : System.IO.Path.GetFileName(path).ToLowerInvariant();

        if (name == ".env" || name.EndsWith(".env", StringComparison.Ordinal) || name.StartsWith(".env.", StringComparison.Ordinal))
        {
            return KeyValueFileFormat.Env;
        }

        var trimmed = content?.TrimStart() ?? string.Empty;

        if (trimmed.StartsWith('{'))
        {
            try
            {
                var root = JObject.Parse(trimmed);
                var isDocument = string.Equals(root["Format"]?.Value<string>(), DocumentFormat, StringComparison.Ordinal) || root["Items"] is JArray;
                return isDocument ? KeyValueFileFormat.Json : KeyValueFileFormat.Flat;
            }
            catch (JsonException)
            {
                return KeyValueFileFormat.Json;
            }
        }

        return name.EndsWith(".json", StringComparison.Ordinal) ? KeyValueFileFormat.Json : KeyValueFileFormat.Env;
    }

    // Reads a file into a document; flat and env entries get the level the caller decides per key.
    public static KeyValueDocument Parse(string content, KeyValueFileFormat format, Func<string, KeyValueDocumentItemProtection> levelOf)
    {
        if (format == KeyValueFileFormat.Json)
        {
            var document = JsonConvert.DeserializeObject<KeyValueDocument>(content)
                ?? throw new FormatException("The file is not a key/value document.");

            if (document.Items is null)
            {
                throw new FormatException("The document has no Items.");
            }

            document.Format ??= DocumentFormat;
            return document;
        }

        var entries = format == KeyValueFileFormat.Env ? ParseEnv(content) : ParseFlat(content);

        return new KeyValueDocument
        {
            Format = DocumentFormat,
            Items = entries.Select(entry => new KeyValueDocumentItem
            {
                Key = entry.Key,
                Value = entry.Value,
                Protection = levelOf(entry.Key),
            }).ToList(),
        };
    }

    // Writes a document in the format; flat and env carry values only, so secrets without a value are left out.
    public static string Serialize(KeyValueDocument document, KeyValueFileFormat format, out IReadOnlyList<string> skippedKeys)
    {
        var items = document.Items ?? [];

        if (format == KeyValueFileFormat.Json)
        {
            skippedKeys = [];
            return JsonConvert.SerializeObject(document, Formatting.Indented);
        }

        skippedKeys = items.Where(item => item.Value is null).Select(item => item.Key).ToList();
        var entries = items.Where(item => item.Value is not null).Select(item => (item.Key, item.Value!)).ToList();

        return format == KeyValueFileFormat.Env ? SerializeEnv(entries) : SerializeFlat(entries);
    }

    // --- .env ---

    public static IReadOnlyList<(string Key, string Value)> ParseEnv(string text)
    {
        var entries = new List<(string Key, string Value)>();
        var lines = text.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r').Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');

            if (separator <= 0)
            {
                throw new FormatException($"Line {index + 1} is not KEY=VALUE: {line}");
            }

            var key = line[..separator].Trim();

            if (key.StartsWith("export ", StringComparison.Ordinal))
            {
                key = key["export ".Length..].Trim();
            }

            var rest = line[(separator + 1)..].Trim();
            string value;

            if (rest.StartsWith('"'))
            {
                // a double-quoted value may continue on the next lines
                var end = FindClosingQuote(rest);

                while (end < 0 && index + 1 < lines.Length)
                {
                    index++;
                    rest += "\n" + lines[index].TrimEnd('\r');
                    end = FindClosingQuote(rest);
                }

                if (end < 0)
                {
                    throw new FormatException($"Line {index + 1} has an unterminated quoted value.");
                }

                value = Unescape(rest[1..end]);
            }
            else if (rest.StartsWith('\''))
            {
                var end = rest.IndexOf('\'', 1);

                if (end < 0)
                {
                    throw new FormatException($"Line {index + 1} has an unterminated quoted value.");
                }

                value = rest[1..end];
            }
            else
            {
                var comment = InlineComment().Match(rest);
                value = (comment.Success ? rest[..comment.Index] : rest).Trim();
            }

            entries.Add((FromEnvKey(key), value));
        }

        return entries;
    }

    public static string SerializeEnv(IEnumerable<(string Key, string Value)> entries)
    {
        return string.Join('\n', entries.Select(entry => $"{ToEnvKey(entry.Key)}={QuoteIfNeeded(entry.Value)}"));
    }

    // db.password → DB__PASSWORD
    public static string ToEnvKey(string key) => key.Replace(".", "__", StringComparison.Ordinal).ToUpperInvariant();

    // DB__PASSWORD → db.password
    public static string FromEnvKey(string key) => key.Replace("__", ".", StringComparison.Ordinal).ToLowerInvariant();

    // --- flat JSON ---

    public static IReadOnlyList<(string Key, string Value)> ParseFlat(string json)
    {
        var root = JsonConvert.DeserializeObject<JToken>(json) as JObject
            ?? throw new FormatException("The flat JSON must be an object.");
        var entries = new List<(string Key, string Value)>();
        Flatten(root, string.Empty, entries);
        return entries;
    }

    public static string SerializeFlat(IEnumerable<(string Key, string Value)> entries)
    {
        var root = new JObject();

        foreach (var (key, value) in entries)
        {
            root[key] = value;
        }

        return root.ToString(Formatting.Indented);
    }

    private static void Flatten(JToken token, string prefix, List<(string Key, string Value)> entries)
    {
        if (token is JObject @object)
        {
            foreach (var property in @object.Properties())
            {
                Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", entries);
            }

            return;
        }

        if (prefix.Length == 0)
        {
            throw new FormatException("The flat JSON must be an object.");
        }

        var text = token.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => string.Empty,
            JTokenType.String => token.Value<string>() ?? string.Empty,
            JTokenType.Array => token.ToString(Formatting.None),
            _ => token.ToString(Formatting.None),
        };

        entries.Add((prefix, text));
    }

    private static int FindClosingQuote(string text)
    {
        for (var index = 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }

            if (text[index] == '"')
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unescape(string text)
    {
        var builder = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character != '\\' || index + 1 >= text.Length)
            {
                builder.Append(character);
                continue;
            }

            index++;
            builder.Append(text[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                var other => other,
            });
        }

        return builder.ToString();
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Length > 0 && !NeedsQuotes().IsMatch(value))
        {
            return value;
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }

    [GeneratedRegex(@"\s#")]
    private static partial Regex InlineComment();

    [GeneratedRegex("[\\s\"'#=\\\\]")]
    private static partial Regex NeedsQuotes();
}

// Shell-style globs over keys: "*" is any run of characters, "?" one character, case-insensitive.
public static class KeyGlob
{
    public static bool IsMatch(string key, string glob)
    {
        var pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(key, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    public static bool IsMatch(string key, IEnumerable<string>? globs)
    {
        return globs is not null && globs.Any(glob => IsMatch(key, glob));
    }
}

public static class KeyValueTimes
{
    public static string Format(DateTimeOffset? time)
    {
        return time is null ? "—" : time.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string FormatExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null)
        {
            return "—";
        }

        var days = (expiresAt.Value - now).TotalDays;
        return days < 1 ? "expires today" : $"expires in {Math.Floor(days):0} days";
    }
}
