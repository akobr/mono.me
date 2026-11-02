using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// section#trusted-issuers of the Machines page (TrustedIssuersTable.vue, Administrator and up; the target of
// "Add a trusted issuer" in New machine): "Add trusted issuer", the info alert "Federated machines are disabled" while
// the machine policy does not enable Federated, the empty state "No trusted issuers", and a table with the columns
// Name (the name and the description), Issuer, Key source ("Discovery", "JWKS URI", "Pasted JWKS"), Max lifetime ("1 h
// (default)" when unset), Required claims ("<claim> = <value>" per line, or "–"), then "Edit <name>" and
// "Delete <name>". Delete asks "Delete <name>?" with the typed name and toasts "Trusted issuer <name> deleted"; the
// dialog's save toasts "Trusted issuer <name> saved". Trusted issuers are a project-wide list (at most 20): add them only
// in a disposable project and delete them in the cleanup.
public sealed partial class TrustedIssuersSection(App app)
{
    // The columns, in their order; the last one (the actions) has no header.
    private static readonly string[] Columns = ["Name", "Issuer", "Key source", "Max lifetime", "Required claims"];

    public ILocator Root => app.Page.Locator("section#trusted-issuers");

    public ILocator Heading => Root.GetByRole(AriaRole.Heading, new() { Name = "Trusted issuers", Exact = true, Level = 2 });

    public ILocator AddButton => Root.GetByRole(AriaRole.Button, new() { Name = "Add trusted issuer", Exact = true });

    public ILocator FederationDisabled => AccessLocators.Alert(Root, "Federated machines are disabled");

    public ILocator NoTrustedIssuers => Root.GetByRole(AriaRole.Heading, new() { Name = "No trusted issuers", Exact = true });

    public ILocator IssuersTable => Root.GetByRole(AriaRole.Table);

    public ILocator IssuerRows => IssuersTable.Locator("tbody").GetByRole(AriaRole.Row);

    // The dialog of Add trusted issuer.
    public TrustedIssuerDialog AddDialog => new(app);

    // The row of a trusted issuer, by its "Edit <name>" button.
    public ILocator IssuerRow(string name)
    {
        return IssuerRows.Filter(new() { Has = EditButtonOf(name) });
    }

    // A cell of an issuer's row by its column: Name, Issuer, Key source, Max lifetime or Required claims.
    public ILocator IssuerCell(string name, string column)
    {
        var index = Array.IndexOf(Columns, column);
        return index >= 0
            ? IssuerRow(name).GetByRole(AriaRole.Cell).Nth(index)
            : throw new ArgumentException($"'{column}' is not a column of the trusted issuers: {string.Join(", ", Columns)}.", nameof(column));
    }

    // The description under an issuer's name.
    public ILocator IssuerDescription(string name)
    {
        return IssuerCell(name, "Name").Locator("p");
    }

    public ILocator EditButton(string name)
    {
        return IssuerRow(name).GetByRole(AriaRole.Button, new() { Name = $"Edit {name}", Exact = true });
    }

    public ILocator DeleteButton(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return IssuerRow(name).GetByRole(AriaRole.Button, new() { Name = $"Delete {name}", Exact = true });
    }

    // "Delete <name>?" with the typed name, confirmed with "Delete".
    public ConfirmDialog DeleteDialog(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ConfirmDialog(app, $"Delete {name}?");
    }

    // The dialog of "Edit <name>".
    public TrustedIssuerDialog EditDialog(string name)
    {
        return new TrustedIssuerDialog(app, name);
    }

    // Waits until the section shows its table or its empty state.
    public async Task WaitForIssuersAsync()
    {
        await Expect(IssuersTable.Or(NoTrustedIssuers)).ToBeVisibleAsync();
    }

    // Opens "Add a trusted issuer" (it starts from the GitHub Actions template).
    public async Task<TrustedIssuerDialog> OpenAddDialogAsync()
    {
        await AddButton.ClickAsync();
        var dialog = AddDialog;
        await Expect(dialog.Root).ToBeVisibleAsync();
        return dialog;
    }

    // Adds a GitHub Actions trusted issuer with the name and the accepted repository_owner_id: the setup of a test
    // that needs one. Returns once the toast "Trusted issuer <name> saved" showed, the dialog closed and the row is
    // listed.
    public async Task AddGitHubIssuerAsync(string name, string repositoryOwnerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryOwnerId);

        var dialog = await OpenAddDialogAsync();
        await Expect(dialog.Template).ToHaveTextAsync("GitHub Actions");
        await dialog.Name.FillAsync(name);
        await dialog.ClaimValue(1).FillAsync(repositoryOwnerId);
        await dialog.SubmitAsync(name);
        await Expect(IssuerRow(name)).ToBeVisibleAsync();
    }

    // Deletes a trusted issuer: "Delete <name>", the typed name, "Delete"; returns once the toast "Trusted issuer
    // <name> deleted" showed and the row is gone.
    public async Task DeleteIssuerAsync(string name)
    {
        await DeleteButton(name).ClickAsync();
        await DeleteDialog(name).TypeAndConfirmAsync("Delete");
        await Expect(app.Toast($"Trusted issuer {name} deleted")).ToBeVisibleAsync();
        await Expect(IssuerRow(name)).ToHaveCountAsync(0);
    }

    // For a cleanup: deletes the trusted issuer when the section lists it, else nothing.
    public async Task DeleteIssuerIfListedAsync(string name)
    {
        await WaitForIssuersAsync();
        if (await IssuerRow(name).CountAsync() > 0)
        {
            await DeleteIssuerAsync(name);
        }
    }

    private ILocator EditButtonOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = $"Edit {name}", Exact = true });
    }
}
