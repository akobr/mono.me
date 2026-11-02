using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// A rule of a Federated machine in its editor (FederationRuleEditor.vue, in New machine and in "Federation rules"): the
// group "Rule <n>" with "Remove rule <n>" (only with more than one rule), the select "Trusted issuer" (placeholder "Pick
// a trusted issuer"; preselected when the project has exactly one; its help names the issuer's required claims),
// "Audience" (prefilled with the project's token endpoint), "Subject" and the group "Claims". Required fields'
// accessible names end in '*'.
public sealed partial class FederationRuleGroup(ILocator scope, int number)
{
    public ILocator Root => scope.GetByRole(AriaRole.Group, new() { Name = $"Rule {number}", Exact = true });

    public ILocator TrustedIssuer => Root.GetByRole(AriaRole.Combobox, new() { NameRegex = TrustedIssuerLabel() });

    // The help of "Trusted issuer": "Every token of this issuer must also carry <claim> = <value>."
    public ILocator TrustedIssuerHelp => NuxtLocators.FieldHelp(Root, "Trusted issuer");

    public ILocator Audience => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = AudienceLabel() });

    public ILocator Subject => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = SubjectLabel() });

    public ILocator RemoveButton => Root.GetByRole(AriaRole.Button, new() { Name = $"Remove rule {number}", Exact = true });

    [GeneratedRegex(@"^Trusted issuer\*?$")]
    private static partial Regex TrustedIssuerLabel();

    [GeneratedRegex(@"^Audience\*?$")]
    private static partial Regex AudienceLabel();

    [GeneratedRegex(@"^Subject\*?$")]
    private static partial Regex SubjectLabel();
}
