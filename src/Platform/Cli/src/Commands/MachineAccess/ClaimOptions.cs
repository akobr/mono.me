using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Claim pins as the CLI takes them (--claim and --require-claim): key=value, split at the first '='. The key is
// a claim name (repository_id) or a JSON pointer to a nested claim (/kubernetes.io/namespace). A repeated key
// adds an accepted value. Whether a key or a value is allowed is for the server to say.
internal static class ClaimOptions
{
    // Null claims when no value is given, so the request leaves them out.
    public static bool TryParse(
        IEnumerable<string>? values,
        string optionName,
        out IDictionary<string, IReadOnlyList_String>? claims,
        [NotNullWhen(false)] out string? error)
    {
        claims = null;
        error = null;

        foreach (var value in values ?? [])
        {
            var separator = value.IndexOf('=');
            var key = separator < 0 ? string.Empty : value[..separator].Trim();
            var accepted = separator < 0 ? string.Empty : value[(separator + 1)..].Trim();

            if (key.Length < 1 || accepted.Length < 1)
            {
                claims = null;
                error = $"{optionName} takes key=value, for example repository_id=74 or /kubernetes.io/namespace=billing, not '{value}'.";
                return false;
            }

            claims ??= new Dictionary<string, IReadOnlyList_String>(StringComparer.Ordinal);

            if (!claims.TryGetValue(key, out var acceptedValues))
            {
                acceptedValues = [];
                claims[key] = acceptedValues;
            }

            if (!acceptedValues.Contains(accepted))
            {
                acceptedValues.Add(accepted);
            }
        }

        return true;
    }
}
