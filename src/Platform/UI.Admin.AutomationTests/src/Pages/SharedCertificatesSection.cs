using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// section#shared-certificates of the Machines page (SharedCertificatesTable.vue, Administrator and up; where the old
// …/certificates redirects): "Issue certificate", disabled with the info alert "Shared certificates are disabled" while
// the machine policy does not enable CertificateAndApiKey, the empty state "No shared certificates", and a table with
// the columns Label, Thumbprint (the first 16 characters and "…"), Valid from, Expires and Status ("Valid", "Expires in
// <n> d" within 30 days, "Expired", "Revoked"), then "Revoke" for a certificate that is not revoked. Issue shows the
// certificate once (OneTimeSecretDialog "Shared certificate issued"); Revoke asks "Revoke <label>?" with the first 8
// characters of the thumbprint typed and toasts "Certificate revoked". Revoked certificates stay listed: there is no
// delete.
public sealed partial class SharedCertificatesSection(App app)
{
    // The columns, in their order; the last one (the actions) has no header.
    private static readonly string[] Columns = ["Label", "Thumbprint", "Valid from", "Expires", "Status"];

    public ILocator Root => app.Page.Locator("section#shared-certificates");

    public ILocator Heading => Root.GetByRole(AriaRole.Heading, new() { Name = "Shared certificates", Exact = true, Level = 2 });

    public ILocator IssueButton => Root.GetByRole(AriaRole.Button, new() { Name = "Issue certificate", Exact = true });

    public ILocator CertificatesDisabled => AccessLocators.Alert(Root, "Shared certificates are disabled");

    public ILocator NoSharedCertificates => Root.GetByRole(AriaRole.Heading, new() { Name = "No shared certificates", Exact = true });

    public ILocator CertificatesTable => Root.GetByRole(AriaRole.Table);

    public ILocator CertificateRows => CertificatesTable.Locator("tbody").GetByRole(AriaRole.Row);

    public IssueCertificateDialog IssueDialog => new(app);

    // The row of a shared certificate, by its exact label.
    public ILocator CertificateRow(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return CertificateRows.Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell, new() { Name = label, Exact = true }) });
    }

    // A cell of a certificate's row by its column: Label, Thumbprint, Valid from, Expires or Status.
    public ILocator CertificateCell(string label, string column)
    {
        var index = Array.IndexOf(Columns, column);
        return index >= 0
            ? CertificateRow(label).GetByRole(AriaRole.Cell).Nth(index)
            : throw new ArgumentException($"'{column}' is not a column of the shared certificates: {string.Join(", ", Columns)}.", nameof(column));
    }

    public ILocator RevokeButton(string label)
    {
        return CertificateRow(label).GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true });
    }

    // "Revoke <label>?" with the first 8 characters of the thumbprint typed, confirmed with "Revoke".
    public ConfirmDialog RevokeDialog(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new ConfirmDialog(app, $"Revoke {label}?");
    }

    // Waits until the section shows its table or its empty state.
    public async Task WaitForCertificatesAsync()
    {
        await Expect(CertificatesTable.Or(NoSharedCertificates)).ToBeVisibleAsync();
    }

    // Revokes a certificate through its row: "Revoke", the first 8 characters of the thumbprint (the placeholder of the
    // typed confirmation), "Revoke"; returns once the toast "Certificate revoked" showed and the row is Revoked.
    public async Task RevokeCertificateAsync(string label)
    {
        await RevokeButton(label).ClickAsync();
        await RevokeDialog(label).TypeAndConfirmAsync("Revoke");
        await Expect(app.Toast("Certificate revoked")).ToBeVisibleAsync();
        await Expect(CertificateCell(label, "Status")).ToHaveTextAsync("Revoked");
    }

    // For a cleanup: revokes the certificate when the section lists it unrevoked, else nothing (a revoked certificate
    // stays listed; it cannot be deleted).
    public async Task RevokeCertificateIfValidAsync(string label)
    {
        await WaitForCertificatesAsync();
        if (await RevokeButton(label).CountAsync() > 0)
        {
            await RevokeCertificateAsync(label);
        }
    }

    // Opens "Issue a shared certificate".
    public async Task<IssueCertificateDialog> OpenIssueDialogAsync()
    {
        await IssueButton.ClickAsync();
        var dialog = IssueDialog;
        await Expect(dialog.Root).ToBeVisibleAsync();
        return dialog;
    }
}
