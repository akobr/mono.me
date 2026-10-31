using System.Globalization;
using System.Text.RegularExpressions;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// [TestCase("1234")] is the numeric id of a Testiny test case; a prefixed form such as "TC-1234" (the id Testiny shows
// with the project's key) is accepted and its digits taken.
public static partial class TestinyCaseIds
{
    public static bool TryParse(string? id, out long caseId)
    {
        caseId = 0;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var match = IdPattern().Match(id.Trim());
        return match.Success
            && long.TryParse(match.Groups["digits"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out caseId)
            && caseId > 0;
    }

    // An optional prefix of letters with an optional '-' or '_', then the digits: 1234, TC-1234, TC1234.
    [GeneratedRegex(@"^(?:[A-Za-z]+[-_]?)?(?<digits>[0-9]{1,18})\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
