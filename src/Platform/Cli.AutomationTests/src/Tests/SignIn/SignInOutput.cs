using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.SignIn;

// What the sign-in cases read of sform's output: the name of the account header "# <name> @ <email>" that sform account
// prints after a sign-in (AccountCommand), which sform account logout names again ("You have been logged out from account:
// <name>"); the organizations of a user of several; made-up and tampered API keys. The pool user of a feature case (mode
// X: SGN-DEV-06, SGN-OUT-04) is CliTest's: a [RequiresFeature] of a user feature leases the user the feature stands for
// (CliTest.FeatureUser).
internal static partial class SignInOutput
{
    // The <name> of "# <name> @ <email>"; throws an SformAssertionException with the output when there is no such header.
    public static string AccountNameOf(SformResult result, string email)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var header = new Regex($@"(?m)^#\s+(?<name>.+?)\s+@\s+{Regex.Escape(email)}\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = header.Match(result.PlainStdOut);
        return match.Success ? match.Groups["name"].Value : throw result.Fail($"printed no account header \"# <name> @ {email}\"");
    }

    // The organizations sform lists on standard error when a user of several signs in without -o on redirected input:
    // "  You belong to several organizations; pass one with -o|--organization <id>:", then "    <id>  <name>" for each.
    public static IReadOnlyList<(string Id, string Name)> OrganizationsOf(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return OrganizationLine().Matches(result.PlainStdErr)
            .Select(match => (match.Groups["id"].Value, match.Groups["name"].Value.Trim()))
            .ToArray();
    }

    // A structured API key of the project that no machine has, 2s.<base64url of organization:project:machine id>.<secret>:
    // what sform reads as machine credentials without calling the API (account logout calls nothing for a machine).
    public static string MadeUpApiKey(string projectKey, string machineId, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        var dot = projectKey.IndexOf('.', StringComparison.Ordinal);
        var segment = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{projectKey[..dot]}:{projectKey[(dot + 1)..]}:{machineId}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"2s.{segment}.{secret}";
    }

    // The API key with the last character of its secret changed: the machine segment (organization, project, machine id)
    // stays, so sform signs in without a call, and the API refuses the key on the first call.
    public static string Tampered(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var key = apiKey.Trim();
        return key[..^1] + (key[^1] == 'A' ? 'B' : 'A');
    }

    [GeneratedRegex(@"(?m)^ {4}(?<id>\S+) {2}(?<name>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationLine();
}
