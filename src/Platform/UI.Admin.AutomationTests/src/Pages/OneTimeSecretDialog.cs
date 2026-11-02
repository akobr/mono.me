using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The credentials the API shows once (OneTimeSecretDialog.vue): "Machine access created" after New machine, "New
// credentials" after a reset, "Shared certificate issued" after Issue certificate. "These credentials are shown only
// once. Store them now.", the warning "Shown only once" with what closing discards, the "Id" (a machine id or a
// shared certificate's thumbprint, in a code element), the "API key" with "Copy", "Download .pfx" (<id>.pfx or
// <label>.pfx) and the "Certificate password" for a certificate of its own, the info "Uses a shared certificate" for a
// machine on a shared certificate, and "Ready-to-paste snippets" (curl and .env) with an API key. It cannot be
// dismissed: Escape and a click outside do nothing and it has no close button; "Done" is enabled once "I have stored
// these credentials" is checked. A secret Copy toasts "The clipboard contains a secret".
public sealed partial class OneTimeSecretDialog(App app, string title)
{
    public const string CreatedTitle = "Machine access created";
    public const string ResetTitle = "New credentials";
    public const string IssuedTitle = "Shared certificate issued";
    public const string DescriptionText = "These credentials are shown only once. Store them now.";

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = title, Exact = true });

    public ILocator ShownOnlyOnce => AccessLocators.Alert(Root, "Shown only once");

    // What closing the dialog discards, the description of ShownOnlyOnce.
    public ILocator DiscardHint => AccessLocators.AlertDescription(Root, "Shown only once");

    // The id after "Id": the machine id, or a shared certificate's thumbprint.
    public ILocator Id => Root.GetByText("Id", new() { Exact = true }).Locator("xpath=following-sibling::code[1]");

    public ILocator ApiKeyField => AccessLocators.Field(Root, "API key");

    public ILocator ApiKey => ApiKeyField.Locator("code");

    public ILocator CopyApiKeyButton => ApiKeyField.GetByRole(AriaRole.Button, new() { Name = "Copy", Exact = true });

    public ILocator DownloadPfxButton => Root.GetByRole(AriaRole.Button, new() { Name = "Download .pfx", Exact = true });

    public ILocator CertificatePasswordField => AccessLocators.Field(Root, "Certificate password");

    public ILocator SharedCertificateAlert => AccessLocators.Alert(Root, "Uses a shared certificate");

    public ILocator SnippetsSummary => Root.GetByText("Ready-to-paste snippets", new() { Exact = true });

    public ILocator StoredCheckbox => Root.GetByRole(AriaRole.Checkbox, new() { Name = "I have stored these credentials", Exact = true });

    public ILocator DoneButton => Root.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });

    // Nuxt UI's close button, which this dialog does not have.
    public ILocator CloseButton => Root.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true });

    // The code of a snippet of the open "Ready-to-paste snippets": "curl" or ".env".
    public ILocator Snippet(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.Locator("details > div").Filter(new() { Has = app.Page.GetByText(label, new() { Exact = true }) }).Locator("pre");
    }

    public async Task<string> IdAsync()
    {
        await Expect(Id).ToBeVisibleAsync();
        return (await Id.InnerTextAsync()).Trim();
    }

    public async Task<string> ApiKeyAsync()
    {
        await Expect(ApiKey).ToBeVisibleAsync();
        return (await ApiKey.InnerTextAsync()).Trim();
    }

    // Checks "I have stored these credentials", clicks Done and waits until the dialog closed.
    public async Task AcknowledgeAsync()
    {
        await StoredCheckbox.CheckAsync();
        await DoneButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
