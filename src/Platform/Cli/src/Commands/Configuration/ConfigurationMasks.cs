using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.ConfigurationFormats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Configuration;

// The values of a resolved configuration to mask in the log of a GitHub Actions job, and its unresolved bindings, from
// the values of the resolved and the effective document by JSON path. The read has no secret paths, so under Bound a
// value is possibly secret when resolution produced it: its path has no value in the effective document, or another
// one. That over-masks bound public values and cannot see a secret stored literally, which is what All is for.
internal static class ConfigurationMasks
{
    // Masking 1 or on would hide those characters in the whole log.
    public const int MinimumBoundLength = 4;

    // Every value of the document, without the rules of the formats: an effective document with object bindings may
    // break them.
    public static IReadOnlyList<DocumentLeaf> ReadValues(JToken? content)
    {
        return DocumentFlattener.Read(ConfigurationFormatter.Canonicalize(content), validate: false);
    }

    public static IReadOnlyDictionary<string, string> ByPath(IReadOnlyList<DocumentLeaf> values)
    {
        var byPath = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            byPath.TryAdd(value.Path, value.Value);
        }

        return byPath;
    }

    // A string that starts with @ and is the same in the effective document is a binding nobody evaluated: the caller
    // lacks Secrets.Read, or the API withheld the secrets.
    public static IReadOnlyList<DocumentLeaf> FindUnresolved(IReadOnlyList<DocumentLeaf> resolved, IReadOnlyDictionary<string, string> effective)
    {
        return resolved
            .Where(value => value.Kind == DocumentValueKind.String
                && value.Value.StartsWith('@')
                && effective.TryGetValue(value.Path, out var effectiveValue)
                && string.Equals(effectiveValue, value.Value, StringComparison.Ordinal))
            .ToList();
    }

    // effective: null when it is unknown, and every string and number counts as produced by resolution. written: the
    // format of the --export file of the same run, whose escaped values are masked too.
    public static MaskPlan Plan(IReadOnlyList<DocumentLeaf> resolved, IReadOnlyDictionary<string, string>? effective, MaskMode mode, ConfigurationFormat? written = null)
    {
        var masked = new List<DocumentLeaf>();
        var tooShort = new List<DocumentLeaf>();

        foreach (var value in resolved)
        {
            // The runner refuses to mask a blank value.
            if (value.Kind == DocumentValueKind.Boolean || string.IsNullOrWhiteSpace(value.Value))
            {
                continue;
            }

            if (mode == MaskMode.All)
            {
                masked.Add(value);
                continue;
            }

            var isBound = effective is null
                || !effective.TryGetValue(value.Path, out var effectiveValue)
                || !string.Equals(effectiveValue, value.Value, StringComparison.Ordinal);

            if (!isBound)
            {
                continue;
            }

            if (value.Value.Length < MinimumBoundLength)
            {
                tooShort.Add(value);
                continue;
            }

            masked.Add(value);
        }

        return new MaskPlan(masked, tooShort, MaskData(masked, written));
    }

    // Each distinct value, and every line of a multi-line value, as the runner masks a single line of it otherwise. The
    // lines are masked whatever their length: a short one is still a part of a secret. With a file in properties, env or
    // yaml, also the value as that file writes it, where the runner would not find it.
    private static IReadOnlyList<string> MaskData(IEnumerable<DocumentLeaf> masked, ConfigurationFormat? written)
    {
        var data = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in masked)
        {
            if (seen.Add(value.Value))
            {
                data.Add(value.Value);
            }

            if (written is { } format && WrittenValue(value.Value, format) is { } writtenValue && seen.Add(writtenValue))
            {
                data.Add(writtenValue);
            }

            if (value.Value.AsSpan().IndexOfAny('\r', '\n') < 0)
            {
                continue;
            }

            foreach (var line in value.Value.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
            {
                if (!string.IsNullOrWhiteSpace(line) && seen.Add(line))
                {
                    data.Add(line);
                }
            }
        }

        return data;
    }

    // The runner hides a masked value also in a few encodings of it, its JSON escape among them, which is how flat and
    // the JSON content write it. properties writes every character outside printable ASCII as \uXXXX (U+00E4 as
    // \u00E4), yaml its control characters with upper-case hex, and env keeps them raw between double quotes, which
    // escape a backslash. Null when the file writes the value or its JSON escape.
    private static string? WrittenValue(string value, ConfigurationFormat format)
    {
        if (format is not (ConfigurationFormat.Properties or ConfigurationFormat.Env or ConfigurationFormat.Yaml))
        {
            return null;
        }

        // "v=" and the value in properties, "V=" in env, "v: " in yaml, and a line end.
        var line = ConfigurationFormatter.Format(new JObject { ["v"] = value }, format).Text;
        var text = line[(format == ConfigurationFormat.Yaml ? 3 : 2)..^1];

        if (text.Length >= 2 && text[0] is '"' or '\'' && text[^1] == text[0])
        {
            text = text[1..^1];
        }

        return text == value || text == JsonConvert.ToString(value)[1..^1] ? null : text;
    }
}
