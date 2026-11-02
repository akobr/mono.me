using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Federation rules" (FederationRulesDialog.vue), opened by "Edit rules" of a Federated machine's detail: "A platform
// token must match one of the rules to be exchanged for <id>. Changes apply to new exchanges.", the rules
// (FederationRuleGroup) with "Add rule" (a new rule takes the audience of the machine's rules), the API's refusal as an
// alert, "Cancel" and "Save rules" (the toast "Federation rules saved"; disabled until every rule is complete).
public sealed partial class FederationRulesDialog(App app, string machineId)
{
    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Federation rules", Exact = true });

    public string DescriptionText => $"A platform token must match one of the rules to be exchanged for {machineId}. Changes apply to new exchanges.";

    public ILocator AddRuleButton => Root.GetByRole(AriaRole.Button, new() { Name = "Add rule", Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    public ILocator SaveRulesButton => Root.GetByRole(AriaRole.Button, new() { Name = "Save rules", Exact = true });

    public FederationRuleGroup Rule(int number)
    {
        return new FederationRuleGroup(app, Root, number);
    }

    // The error alert of a refused save, by the API's message.
    public ILocator Failure(string message)
    {
        return AccessLocators.Message(Root, message);
    }

    public async Task WaitForOpenAsync()
    {
        await Expect(Root).ToBeVisibleAsync();
        await Expect(Rule(1).Root).ToBeVisibleAsync();
    }

    // Saves the rules and waits for the toast "Federation rules saved" and the dialog to close.
    public async Task SaveAsync()
    {
        await SaveRulesButton.ClickAsync();
        await Expect(app.Toast("Federation rules saved")).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
