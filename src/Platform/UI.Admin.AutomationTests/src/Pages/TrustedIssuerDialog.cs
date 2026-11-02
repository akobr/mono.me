using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Add a trusted issuer" or "Edit trusted issuer <name>" (TrustedIssuerDialog.vue): "Federated machines of the project
// exchange tokens of this issuer for Storyteller tokens.", "Template" (add only: GitHub Actions, Kubernetes, Vercel,
// Azure managed identity, AWS, Google Cloud, Custom; the dialog opens with GitHub Actions) and an info alert titled by
// the template with its help, "Name" (the template's name while it is not changed, numbered when taken; disabled when
// editing), "Key source" (Discovery, JWKS URI, Pasted JWKS, with a help per source), "Issuer" (read-only when editing),
// "JWKS URI" or "JWKS" for their key sources, "Max token lifetime (seconds)" (a number with a grouping comma, e.g.
// 7,200), the group "Required claims" (textboxes "Claim <n>" and "Value <n>"), "Description", the API's refusal as an
// alert, "Cancel" and "Add issuer" or "Save" (disabled until complete). A save toasts "Trusted issuer <name> saved" and
// closes the dialog. Required fields' accessible names end in '*'.
public sealed partial class TrustedIssuerDialog
{
    public const string DescriptionText = "Federated machines of the project exchange tokens of this issuer for Storyteller tokens.";

    private readonly App app;

    // editing: the name of the trusted issuer that "Edit <name>" opened; null for "Add trusted issuer".
    public TrustedIssuerDialog(App app, string? editing = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        this.app = app;
        Root = app.Page.GetByRole(AriaRole.Dialog, new() { Name = editing is null ? "Add a trusted issuer" : $"Edit trusted issuer {editing}", Exact = true });
        SubmitButton = Root.GetByRole(AriaRole.Button, new() { Name = editing is null ? "Add issuer" : "Save", Exact = true });
    }

    public ILocator Root { get; }

    // "Add issuer", or "Save" when editing.
    public ILocator SubmitButton { get; }

    public ILocator Template => Root.GetByRole(AriaRole.Combobox, new() { Name = "Template", Exact = true });

    public ILocator Name => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = NameLabel() });

    public ILocator NameError => NuxtLocators.FieldError(Root, "Name");

    public ILocator KeySource => Root.GetByRole(AriaRole.Combobox, new() { Name = "Key source", Exact = true });

    public ILocator KeySourceHelp => NuxtLocators.FieldHelp(Root, "Key source");

    public ILocator Issuer => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = IssuerLabel() });

    public ILocator IssuerHelp => NuxtLocators.FieldHelp(Root, "Issuer");

    public ILocator IssuerError => NuxtLocators.FieldError(Root, "Issuer");

    public ILocator JwksUri => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = JwksUriLabel() });

    public ILocator Jwks => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = JwksLabel() });

    public ILocator JwksHelp => NuxtLocators.FieldHelp(Root, "JWKS");

    public ILocator MaxTokenLifetime => Root.GetByLabel("Max token lifetime (seconds)", new() { Exact = true });

    public ILocator RequiredClaims => Root.GetByRole(AriaRole.Group, new() { Name = "Required claims", Exact = true });

    public ILocator Description => Root.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // The description of a template's info alert, which is titled by the template's label ("GitHub Actions",
    // "Kubernetes", "Custom", …).
    public ILocator TemplateHelp(string label)
    {
        return NuxtLocators.AlertDescription(Root, label);
    }

    // The textbox of a required claim's name: "Claim <n>".
    public ILocator ClaimName(int number)
    {
        return RequiredClaims.GetByRole(AriaRole.Textbox, new() { Name = $"Claim {number}", Exact = true });
    }

    // The textbox of a required claim's accepted value: "Value <n>".
    public ILocator ClaimValue(int number)
    {
        return RequiredClaims.GetByRole(AriaRole.Textbox, new() { Name = $"Value {number}", Exact = true });
    }

    // The error under a required claim's row, e.g. "Enter the accepted value of repository_owner_id."
    public ILocator ClaimError(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return RequiredClaims.GetByText(text, new() { Exact = true });
    }

    // The error alert of a refused save, by the API's message.
    public ILocator Failure(string message)
    {
        return NuxtLocators.Message(Root, message);
    }

    // Picks a template by its exact label; the name, the issuer, the key source, the lifetime and the required claims
    // follow it.
    public async Task PickTemplateAsync(string label)
    {
        await app.PickOptionAsync(Template, label);
    }

    // Picks a key source by its label ("Discovery", "JWKS URI", "Pasted JWKS"); an option's name continues with its
    // description, so it is matched by its start.
    public async Task PickKeySourceAsync(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        await KeySource.ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($"^{Regex.Escape(label)}") }).ClickAsync();
        await Expect(KeySource).ToHaveTextAsync(label);
    }

    // Saves the dialog with "Add issuer" or "Save" and waits for the toast "Trusted issuer <name> saved" and the dialog
    // to close.
    public async Task SubmitAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await SubmitButton.ClickAsync();
        await Expect(app.Toast($"Trusted issuer {name} saved")).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    [GeneratedRegex(@"^Name\*?$")]
    private static partial Regex NameLabel();

    [GeneratedRegex(@"^Issuer\*?$")]
    private static partial Regex IssuerLabel();

    [GeneratedRegex(@"^JWKS URI\*?$")]
    private static partial Regex JwksUriLabel();

    [GeneratedRegex(@"^JWKS\*?$")]
    private static partial Regex JwksLabel();
}
