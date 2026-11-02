using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The detail of a machine (/<org>/<project>/machines/<id>, section#machine-detail named by the id), over or beside the
// Machines list (DetailPanel): below 768 px full screen and the list hidden, from 768 px an overlay with "Open in full
// screen", from 1024 px also "Pin the detail"; its header has the id (h2) and "Close" at every width, which goes back
// to the list. A list of terms (dt) and values (dd): "Runtime scopes" and "Management scopes" (a badge and the
// description per scope, or "None"), "Annotation" (the key or "any annotation"), "Credentials", "Object id", and for a
// machine that is not Federated "Certificate" ("A shared certificate of the project", the thumbprint or "–"),
// "Certificate lifetime" ("<n> days"), "Certificate expires" and "Last renewal"; a Federated machine has "Token
// endpoint" and the region "Federation rules" with "Setup", "Edit rules" (only for a user with the role its scopes
// need) and a list item per rule ("Rule <n>", the issuer's badge, Audience, Subject, claims). An unknown id shows
// "Not found" with "Retry".
public sealed partial class MachineDetail(App app, AppRoutes routes, string id)
{
    public DetailPanel Panel => new(app, "machine-detail");

    public ILocator Root => Panel.Root;

    public ILocator Heading => Root.GetByRole(AriaRole.Heading, new() { Name = id, Exact = true, Level = 2 });

    // The header's close button, at every width.
    public ILocator CloseButton => Root.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true });

    public ILocator NotFound => Root.GetByRole(AriaRole.Heading, new() { Name = "Not found", Exact = true });

    public ILocator RetryButton => Root.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    public ILocator FederationRules => Root.GetByRole(AriaRole.Region, new() { Name = "Federation rules", Exact = true });

    public ILocator SetupButton => FederationRules.GetByRole(AriaRole.Button, new() { Name = "Setup", Exact = true });

    public ILocator EditRulesButton => FederationRules.GetByRole(AriaRole.Button, new() { Name = "Edit rules", Exact = true });

    // The rules' list items.
    public ILocator RuleItems => FederationRules.Locator("li");

    public FederatedSetupDialog SetupDialog => new(app, $"Set up {id}");

    public FederationRulesDialog RulesDialog => new(app, id);

    // The value (dd) of a term of the detail's list: "Runtime scopes", "Management scopes", "Annotation",
    // "Credentials", "Object id", "Certificate", "Certificate lifetime", "Certificate expires", "Last renewal" or
    // "Token endpoint".
    public ILocator Value(string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return Root.Locator($"dl > dt:text-is(\"{term}\") + dd");
    }

    // The description of a scope in "Runtime scopes" or "Management scopes".
    public ILocator ScopeDescription(string term, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return Value(term).Locator("li").Filter(new() { Has = app.Page.GetByText(scope, new() { Exact = true }) }).Locator("p");
    }

    // The item of a rule in "Federation rules", by its number.
    public ILocator RuleItem(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return RuleItems.Filter(new() { Has = app.Page.GetByText($"Rule {number}", new() { Exact = true }) });
    }

    // The issuer's badge of a rule.
    public ILocator RuleIssuer(int number)
    {
        return RuleItem(number).Locator("[data-slot='label']").First;
    }

    // A value of a rule: "Audience", "Subject" or a claim.
    public ILocator RuleValue(int number, string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return RuleItem(number).Locator($"dt:text-is(\"{term}\") + dd");
    }

    // Opens the detail by its route and waits for it (its heading, then its terms or an error state). Not for the
    // user menu: below 768 px the full-screen detail covers the list's navbar, and with it the user menu.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Machine(id));
        await WaitForAsync();
    }

    // Waits until the detail shows its heading and has loaded (its list of terms, or an error state).
    public async Task WaitForAsync()
    {
        await Expect(Heading).ToBeVisibleAsync();
        await Expect(Value("Credentials").Or(RetryButton)).ToBeVisibleAsync();
    }

    // Closes the detail with "Close" and waits for the Machines list's URL.
    public async Task CloseAsync()
    {
        await CloseButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
        await app.ExpectAtAsync(routes.Machines);
    }
}
