using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<org>/<project>/machines (the sidebar's "Machines", Contributor and up): the section "Machine access" with the
// machines, then, for an Administrator or Owner only, section#shared-certificates and section#trusted-issuers. The top
// bar has "New machine" for a Contributor and up (its label stays on a phone); an empty list shows "No machine access"
// with a second "New machine". The machines are a plain table at every width with the columns Id (the link to the
// detail, named by the id), Scopes (a badge per scope string, in the API's order), Annotation ("any" or the key),
// Credentials ("API key", "Certificate", "Certificate and API key", "Client credentials", "Federated"), Certificate
// (the thumbprint's first 12 characters and "…" with a "<n> d" badge, "shared", or "–") and Renewed, then the row's
// "Reset" (a kind with a secret: not Federated or Certificate) and "Delete <id>", both only for a user with the role
// the machine's scopes need. Reset asks "Reset <id>?" and shows the new credentials once ("New credentials"); Delete
// asks "Delete <id>?" with the typed id and toasts "Machine <id> deleted". The ids are machine ids (mach-<hex> on the
// mock, GUIDs on a deployed target): find a row by its id, never by its index. Below Contributor the section shows
// "No access" with "Retry": the page does not ask for the list (the API refuses it, but a Debug API skips its role
// checks of users), and Retry reads the role again. The section headings are upper case only through CSS.
public sealed partial class MachinesPage(App app, AppRoutes routes)
{
    // The columns of the machines, in their order; the last one (the actions) has no header.
    private static readonly string[] Columns = ["Id", "Scopes", "Annotation", "Credentials", "Certificate", "Renewed"];

    public ILocator MachineAccessHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Machine access", Exact = true, Level = 2 });

    public ILocator MachineAccessSection => app.Page.Locator("section").Filter(new() { Has = MachineAccessHeading });

    // The navbar's "New machine"; an empty list has a second one in its empty state, after it.
    public ILocator NewMachineButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New machine", Exact = true }).First;

    public ILocator MachinesTable => MachineAccessSection.GetByRole(AriaRole.Table);

    // The machines' rows, without the header.
    public ILocator MachineRows => MachinesTable.Locator("tbody").GetByRole(AriaRole.Row);

    public ILocator NoMachineAccess => MachineAccessSection.GetByRole(AriaRole.Heading, new() { Name = "No machine access", Exact = true });

    // The refusal of the list below Contributor, shown by the page without asking, as when the API refuses (403).
    public ILocator NoAccess => MachineAccessSection.GetByRole(AriaRole.Heading, new() { Name = "No access", Exact = true });

    public ILocator RetryButton => MachineAccessSection.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    public SharedCertificatesSection SharedCertificates => new(app);

    public TrustedIssuersSection TrustedIssuers => new(app);

    public MachineCreateDialog CreateDialog => new(app, routes);

    // The row of a machine, by its id link.
    public ILocator MachineRow(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return MachineRows.Filter(new() { Has = app.Page.GetByRole(AriaRole.Link, new() { Name = id, Exact = true }) });
    }

    // The id link of a machine's row, to its detail.
    public ILocator MachineLink(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return MachinesTable.GetByRole(AriaRole.Link, new() { Name = id, Exact = true });
    }

    // A cell of a machine's row by its column: Id, Scopes, Annotation, Credentials, Certificate or Renewed.
    public ILocator MachineCell(string id, string column)
    {
        var index = Array.IndexOf(Columns, column);
        return index >= 0
            ? MachineRow(id).GetByRole(AriaRole.Cell).Nth(index)
            : throw new ArgumentException($"'{column}' is not a column of the machines: {string.Join(", ", Columns)}.", nameof(column));
    }

    // The scope badges of a machine's row, in the API's order (runtime scopes, then management scopes).
    public ILocator ScopeBadges(string id)
    {
        return MachineCell(id, "Scopes").Locator("[data-slot='label']");
    }

    // The thumbprint start of a machine's own certificate ("<12 characters>…", the full one in its title).
    public ILocator CertificateThumbprint(string id)
    {
        return MachineCell(id, "Certificate").Locator("code");
    }

    // The badge of a machine's certificate: "<n> d" (days left), "expired" or "shared".
    public ILocator CertificateBadge(string id)
    {
        return MachineCell(id, "Certificate").Locator("[data-slot='label']");
    }

    public ILocator ResetButton(string id)
    {
        return MachineRow(id).GetByRole(AriaRole.Button, new() { Name = "Reset", Exact = true });
    }

    public ILocator DeleteButton(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return MachineRow(id).GetByRole(AriaRole.Button, new() { Name = $"Delete {id}", Exact = true });
    }

    // "Reset <id>?": the current secret stops working, the new one is shown once; confirmed with "Reset credentials".
    public ConfirmDialog ResetDialog(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new ConfirmDialog(app, $"Reset {id}?");
    }

    // "Delete <id>?" with the typed id ("Type <id> to confirm", its placeholder the id), confirmed with "Delete".
    public ConfirmDialog DeleteDialog(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new ConfirmDialog(app, $"Delete {id}?");
    }

    // The detail of a machine (section#machine-detail), opened by its id link or by its route.
    public MachineDetail Detail(string id)
    {
        return new MachineDetail(app, routes, id);
    }

    // Opens the page and waits until the list of machines is there: the table, the empty state or the error state.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Machines);
        await app.WaitForReadyAsync();
        await WaitForMachinesAsync();
    }

    // Waits until the section "Machine access" shows the table, its empty state or its error state.
    public async Task WaitForMachinesAsync()
    {
        await Expect(MachinesTable.Or(NoMachineAccess).Or(NoAccess)).ToBeVisibleAsync();
    }

    // Opens "New machine access" with the navbar's "New machine".
    public async Task<MachineCreateDialog> OpenCreateDialogAsync()
    {
        await NewMachineButton.ClickAsync();
        var dialog = CreateDialog;
        await dialog.WaitForOpenAsync();
        return dialog;
    }

    // Creates an API key machine with the preset scopes (Runtime reader), stores its credentials and returns its id and
    // its API key, once the dialog closed and the row is listed: the setup of a test that acts on a machine. The project
    // must enable ApiKey. created gets the id as soon as the machine exists, before the steps that may still fail, so
    // that the test's cleanup deletes it.
    public async Task<(string Id, string ApiKey)> CreateApiKeyMachineAsync(Action<string>? created = null)
    {
        var create = await OpenCreateDialogAsync();
        await create.PickCredentialKindAsync("ApiKey");
        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        created?.Invoke(id);
        var key = await secret.ApiKeyAsync();
        await secret.AcknowledgeAsync();
        await Expect(MachineRow(id)).ToBeVisibleAsync();
        return (id, key);
    }

    // Opens the detail of a machine with its id link and waits for it.
    public async Task<MachineDetail> OpenMachineAsync(string id)
    {
        await MachineLink(id).ClickAsync();
        var detail = Detail(id);
        await app.ExpectAtAsync(routes.Machine(id));
        await detail.WaitForAsync();
        return detail;
    }

    // Deletes a machine through its row: "Delete <id>", the typed id, "Delete"; returns once the toast "Machine <id>
    // deleted" showed and the row is gone.
    public async Task DeleteMachineAsync(string id)
    {
        await DeleteButton(id).ClickAsync();
        await DeleteDialog(id).TypeAndConfirmAsync("Delete");
        await Expect(app.Toast($"Machine {id} deleted")).ToBeVisibleAsync();
        await Expect(MachineRow(id)).ToHaveCountAsync(0);
    }

    // For a cleanup: deletes the machine when the list has it (and the user may delete it), else nothing.
    public async Task DeleteMachineIfListedAsync(string id)
    {
        await WaitForMachinesAsync();
        if (await DeleteButton(id).CountAsync() > 0)
        {
            await DeleteMachineAsync(id);
        }
    }
}
