using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// How a workload uses a Federated machine (FederatedSetupDialog.vue): "Federated machine created" after New machine,
// "Set up <id>" from the detail's "Setup". "This machine has no secret: its workload exchanges a token of a trusted
// issuer for a short-lived Storyteller token.", the fields "Machine id (client_id)" and "Token endpoint" (code elements
// with "Copy to clipboard"), "Accepted tokens" with "Rule <n>: <issuer>, audience <audience>, subject <subject>" per
// rule, and the snippet regions "Token exchange (curl)" and, with a rule of a GitHub issuer, "GitHub Actions step".
// Nothing in it is secret: it is a normal dialog that Escape and "Done" close.
public sealed partial class FederatedSetupDialog(App app, string title)
{
    public const string CreatedTitle = "Federated machine created";
    public const string DescriptionText = "This machine has no secret: its workload exchanges a token of a trusted issuer for a short-lived Storyteller token.";

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = title, Exact = true });

    public ILocator MachineId => NuxtLocators.Field(Root, "Machine id (client_id)").Locator("code");

    public ILocator TokenEndpoint => NuxtLocators.Field(Root, "Token endpoint").Locator("code");

    // The items of "Accepted tokens", one per rule.
    public ILocator AcceptedTokens => Root.GetByText("Accepted tokens", new() { Exact = true }).Locator("xpath=following-sibling::ul[1]/li");

    public ILocator DoneButton => Root.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });

    // A snippet, a focusable region: "Token exchange (curl)" or "GitHub Actions step".
    public ILocator Snippet(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.GetByRole(AriaRole.Region, new() { Name = label, Exact = true });
    }

    public async Task<string> MachineIdAsync()
    {
        await Expect(MachineId).ToBeVisibleAsync();
        return (await MachineId.InnerTextAsync()).Trim();
    }

    public async Task DoneAsync()
    {
        await DoneButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
