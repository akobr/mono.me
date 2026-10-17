using System.Text;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Accessing.Federation;

// Keys of FederationRule.Claims and FederatedIssuer.RequiredClaims: a top-level claim name, or an RFC 6901
// JSON pointer to a nested claim ("/kubernetes.io/namespace", "/https:~1~1sts.amazonaws.com~1/aws_account").
public static partial class FederationClaimKeys
{
    public const int MaxNameLength = 128;

    // Characters after the leading '/'.
    public const int MaxPointerLength = 256;

    // Checked by the verification or pinned through FederationRule.Subject and Audience.
    private static readonly HashSet<string> ReservedClaims = new(StringComparer.Ordinal)
    {
        "iss", "sub", "aud", "exp", "nbf", "iat", "jti",
    };

    public static bool IsPointer(string key) => key.StartsWith('/');

    // The path of the claim a key selects: the name itself, or the decoded reference tokens of a pointer.
    // False when the key is neither a valid claim name nor a valid pointer.
    public static bool TryGetPath(string? key, out IReadOnlyList<string> path)
    {
        path = [];

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (!IsPointer(key))
        {
            if (!ClaimNamePattern().IsMatch(key))
            {
                return false;
            }

            path = [key];
            return true;
        }

        return key.Length - 1 is >= 1 and <= MaxPointerLength
            && TryDecodePointer(key, out path);
    }

    // RFC 6901: '/' separates reference tokens, "~1" stands for '/' and "~0" for '~'; any other '~' sequence is invalid.
    public static bool TryDecodePointer(string pointer, out IReadOnlyList<string> segments)
    {
        segments = [];

        if (!pointer.StartsWith('/'))
        {
            return false;
        }

        var result = new List<string>();
        var current = new StringBuilder();

        for (var index = 1; index < pointer.Length; index++)
        {
            var character = pointer[index];

            if (character == '/')
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else if (character == '~')
            {
                var escaped = index + 1 < pointer.Length ? pointer[++index] : '\0';

                switch (escaped)
                {
                    case '0':
                        current.Append('~');
                        break;
                    case '1':
                        current.Append('/');
                        break;
                    default:
                        return false;
                }
            }
            else
            {
                current.Append(character);
            }
        }

        result.Add(current.ToString());
        segments = result;
        return true;
    }

    // Whether the key selects the top-level claim name, written bare or as a one-token pointer.
    public static bool IsTopLevel(string key, string claimName)
    {
        return TryGetPath(key, out var path) && path.Count == 1 && string.Equals(path[0], claimName, StringComparison.Ordinal);
    }

    // Null when the key can be pinned, else why not (a clause that starts in lower case).
    public static string? Validate(string? key)
    {
        if (!TryGetPath(key, out var path))
        {
            return $"the claim key '{key}' is neither a claim name (1 to {MaxNameLength} letters, digits, '_', '.', ':' or '-') "
                + $"nor a JSON pointer ('/' and 1 to {MaxPointerLength} characters, with ~0 and ~1 as the only escapes).";
        }

        if (path.Count == 1 && ReservedClaims.Contains(path[0]))
        {
            return $"the claim {key} cannot be pinned; use Subject and Audience.";
        }

        return null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClaimNamePattern();
}
