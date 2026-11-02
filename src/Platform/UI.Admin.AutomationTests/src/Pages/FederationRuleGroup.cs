using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// A rule of a Federated machine in its editor (FederationRuleEditor.vue, in New machine and in "Federation rules"): the
// group "Rule <n>" with "Remove rule <n>" (only with more than one rule), the select "Trusted issuer" (placeholder "Pick
// a trusted issuer"; preselected when the project has exactly one; its help names the issuer's required claims),
// "Audience" (prefilled with the project's token endpoint), "Subject" and the group "Claims". Required fields'
// accessible names end in '*'.
public sealed partial class FederationRuleGroup(App app, ILocator scope, int number)
{
    // What the select shows while no trusted issuer is picked.
    public const string NoIssuerText = "Pick a trusted issuer";

    public ILocator Root => scope.GetByRole(AriaRole.Group, new() { Name = $"Rule {number}", Exact = true });

    public ILocator TrustedIssuer => Root.GetByRole(AriaRole.Combobox, new() { NameRegex = TrustedIssuerLabel() });

    // The help of "Trusted issuer": "Every token of this issuer must also carry <claim> = <value>."
    public ILocator TrustedIssuerHelp => AccessLocators.FieldHelp(Root, "Trusted issuer");

    public ILocator Audience => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = AudienceLabel() });

    public ILocator Subject => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = SubjectLabel() });

    public ILocator RemoveButton => Root.GetByRole(AriaRole.Button, new() { Name = $"Remove rule {number}", Exact = true });

    // An option of the open "Trusted issuer" select by the issuer's name (its description, the issuer URL, follows).
    public ILocator TrustedIssuerOption(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($"^{Regex.Escape(name)}(?![a-z0-9-])") });
    }

    [GeneratedRegex(@"^Trusted issuer\*?$")]
    private static partial Regex TrustedIssuerLabel();

    [GeneratedRegex(@"^Audience\*?$")]
    private static partial Regex AudienceLabel();

    [GeneratedRegex(@"^Subject\*?$")]
    private static partial Regex SubjectLabel();
}
