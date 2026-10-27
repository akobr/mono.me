using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.ConfigurationFormats;
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

    // effective: null when it is unknown, and every string and number counts as produced by resolution.
    public static MaskPlan Plan(IReadOnlyList<DocumentLeaf> resolved, IReadOnlyDictionary<string, string>? effective, MaskMode mode)
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

        return new MaskPlan(masked, tooShort, MaskData(masked, mode));
    }

    // Each distinct value, and every line of a multi-line value, as the runner masks a single line of it otherwise.
    private static IReadOnlyList<string> MaskData(IEnumerable<DocumentLeaf> masked, MaskMode mode)
    {
        var data = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in masked)
        {
            if (seen.Add(value.Value))
            {
                data.Add(value.Value);
            }

            if (value.Value.AsSpan().IndexOfAny('\r', '\n') < 0)
            {
                continue;
            }

            foreach (var line in value.Value.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
            {
                if (!string.IsNullOrWhiteSpace(line)
                    && (mode == MaskMode.All || line.Length >= MinimumBoundLength)
                    && seen.Add(line))
                {
                    data.Add(line);
                }
            }
        }

        return data;
    }
}
