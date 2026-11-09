using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// What sform account points prints (AccessPointListCommand, AccessPointGetCommand): a line "- <key> [<role>]" per access
// point of the user, and under "# Access point <key>" a line " - <account id> [<role>]" per member.
internal static partial class AccessPointOutput
{
    // The access points and the members by key or account id, with the role as printed (the AccountRole's name).
    public static IReadOnlyDictionary<string, string> EntriesOf(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in EntryLine().Matches(result.PlainStdOut))
        {
            entries[match.Groups["key"].Value] = match.Groups["role"].Value;
        }

        return entries;
    }

    // The role printed for the key or account id; null when the output does not list it.
    public static string? RoleOf(SformResult result, string keyOrAccountId)
    {
        return EntriesOf(result).TryGetValue(keyOrAccountId, out var role) ? role : null;
    }

    [GeneratedRegex(@"(?m)^\s*-\s+(?<key>\S+)\s+\[(?<role>[A-Za-z]+)\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryLine();
}
