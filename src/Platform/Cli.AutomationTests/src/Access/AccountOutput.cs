using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Access;

// What sform account prints of a signed-in user (AccountCommand): the header "<name> @ <email>", "Account ID: #<id>",
// "You have access to N access points." and the default project, with exit 0, or 43 without a default project.
public static partial class AccountOutput
{
    // The account id of "Account ID: #<id>"; throws an SformAssertionException with the output when the run ended with
    // another code than 0 or 43 or printed no id.
    public static string AccountIdOf(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ExitCode is not (0 or 43))
        {
            throw result.Fail("did not show the account", expected: "0 or 43 (InteractionNeeded: no default project)");
        }

        var match = AccountIdPattern().Match(result.PlainStdOut);
        return match.Success ? match.Groups["id"].Value : throw result.Fail("printed no \"Account ID: #<id>\"");
    }

    [GeneratedRegex(@"Account ID:\s*#(?<id>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex AccountIdPattern();
}
