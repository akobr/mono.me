using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Resolved tab of a configuration (components/configuration/ConfigurationResolved.vue), inside the detail's
// region: the bindings evaluated by the server with the user's role, read again on every visit and never cached. It
// starts with the warning "May contain secrets", whose description depends on the role (SecretsIncludedText from
// ContributorWithSecrets, SecretsLeftOutText below). Then "Evaluating bindings…", the error "A binding could not be
// evaluated" with the server's message and hint (a 422), "No configuration" (a 404), or the read-only editor "Resolved
// configuration", blurred and aria-hidden (so out of the accessibility tree) until "Reveal". Reveal only uncovers what
// was read; another visit of the tab covers it again. A Nuxt UI alert has no alert role, so an alert is the innermost
// component root (data-slot) with its title.
public sealed partial class ConfigurationResolved(App app, ILocator scope)
{
    public const string SecretsTitle = "May contain secrets";

    public const string SecretsIncludedText = "Bindings are evaluated with your role; secret values are included. The response is never cached.";

    public const string SecretsLeftOutText = "Secret values are left out for your role (ContributorWithSecrets is required to see them).";

    public const string BindingErrorTitle = "A binding could not be evaluated";

    public ILocator SecretsAlert => Alert(SecretsTitle);

    public ILocator SecretsAlertTitle => SecretsAlert.GetByText(SecretsTitle, new() { Exact = true });

    public ILocator SecretsAlertDescription => SecretsAlert.Locator("[data-slot=\"description\"]");

    // The 422 of a binding the server cannot evaluate; its description is the server's message and hint.
    public ILocator BindingError => Alert(BindingErrorTitle);

    public ILocator RevealButton => scope.GetByRole(AriaRole.Button, new() { Name = "Reveal", Exact = true });

    // The resolved document; in the accessibility tree only once revealed.
    public CodeEditor Document => new(app, "Resolved configuration", scope);

    // The same editor while it is still covered (aria-hidden), to prove that it is there.
    public ILocator CoveredDocument => scope.GetByRole(AriaRole.Textbox, new() { Name = "Resolved configuration", Exact = true, IncludeHidden = true });

    // Waits until the tab read the resolved document: Reveal, the binding error or "No configuration" shows.
    public async Task WaitForResolvedAsync()
    {
        var noConfiguration = scope.GetByRole(AriaRole.Heading, new() { Name = "No configuration", Exact = true });
        await Expect(SecretsAlertTitle).ToBeVisibleAsync();
        await Expect(RevealButton.Or(BindingError).Or(noConfiguration).First).ToBeVisibleAsync();
    }

    // Uncovers the resolved document and returns its editor, now in the accessibility tree.
    public async Task<CodeEditor> RevealAsync()
    {
        await RevealButton.ClickAsync();
        await Expect(RevealButton).ToHaveCountAsync(0);
        var document = Document;
        await Expect(document.Content).ToBeVisibleAsync();
        return document;
    }

    private ILocator Alert(string title)
    {
        return scope.Locator("[data-slot=\"root\"]")
            .Filter(new() { Has = app.Page.GetByText(title, new() { Exact = true }) })
            .Last;
    }
}
