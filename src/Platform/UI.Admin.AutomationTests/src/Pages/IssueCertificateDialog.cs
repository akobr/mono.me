using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Issue a shared certificate" (SharedCertificatesTable.vue), opened by "Issue certificate": "Label" (required,
// placeholder ci-pipeline), "Lifetime (days)" (1 to 3650, 365 by default), "Cancel" and "Issue", disabled while the
// label is empty. Issue closes it and shows the certificate once (OneTimeSecretDialog "Shared certificate issued").
public sealed partial class IssueCertificateDialog(App app)
{
    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Issue a shared certificate", Exact = true });

    public ILocator Label => Root.GetByRole(AriaRole.Textbox, new() { NameRegex = LabelName() });

    public ILocator Lifetime => Root.GetByLabel("Lifetime (days)", new() { Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // Exact: not the section's "Issue certificate" behind the dialog.
    public ILocator IssueButton => Root.GetByRole(AriaRole.Button, new() { Name = "Issue", Exact = true });

    // Clicks Issue and returns the dialog "Shared certificate issued" once it showed.
    public async Task<OneTimeSecretDialog> IssueAsync()
    {
        await IssueButton.ClickAsync();
        var secret = new OneTimeSecretDialog(app, OneTimeSecretDialog.IssuedTitle);
        await Expect(secret.Root).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
        return secret;
    }

    [GeneratedRegex(@"^Label\*?$")]
    private static partial Regex LabelName();
}
