using System.Text.Json;
using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller;

// The matching of FederationRule and FederatedIssuer.RequiredClaims against a verified token payload.
// Comparisons are ordinal and case-sensitive.
internal static class FederationClaimMatcher
{
    // Exact, or a prefix when the pattern ends with ":*" or "/*" (FederationValidation allows no other wildcard).
    public static bool SubjectMatches(string pattern, string subject)
    {
        return pattern.EndsWith(":*", StringComparison.Ordinal) || pattern.EndsWith("/*", StringComparison.Ordinal)
            ? subject.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(pattern, subject, StringComparison.Ordinal);
    }

    // Null when every pinned claim has an accepted value, else the key of the first one that doesn't.
    public static string? FindMismatch(JsonElement payload, IReadOnlyDictionary<string, IReadOnlyList<string>>? claims)
    {
        if (claims is null)
        {
            return null;
        }

        foreach (var (key, accepted) in claims)
        {
            if (!ClaimMatches(payload, key, accepted))
            {
                return key;
            }
        }

        return null;
    }

    // A string equal to an accepted value, a number or boolean whose JSON text equals one, or an array with any such
    // element. Objects, null and missing claims never match.
    public static bool ClaimMatches(JsonElement payload, string key, IReadOnlyList<string> accepted)
    {
        if (!FederationClaimKeys.TryGetPath(key, out var path) || accepted is null)
        {
            return false;
        }

        var current = payload;

        foreach (var segment in path)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var property))
            {
                current = property;
            }
            else if (current.ValueKind == JsonValueKind.Array && TryGetIndex(segment, current.GetArrayLength(), out var index))
            {
                current = current[index];
            }
            else
            {
                return false;
            }
        }

        return current.ValueKind == JsonValueKind.Array
            ? current.EnumerateArray().Any(element => ScalarMatches(element, accepted))
            : ScalarMatches(current, accepted);
    }

    private static bool ScalarMatches(JsonElement value, IReadOnlyList<string> accepted)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };

        return text is not null && accepted.Contains(text, StringComparer.Ordinal);
    }

    // RFC 6901: an array index is "0" or digits without a leading zero.
    private static bool TryGetIndex(string segment, int length, out int index)
    {
        index = -1;
        return segment.Length > 0
            && (segment == "0" || segment[0] != '0')
            && segment.All(char.IsAsciiDigit)
            && int.TryParse(segment, out index)
            && index < length;
    }
}
