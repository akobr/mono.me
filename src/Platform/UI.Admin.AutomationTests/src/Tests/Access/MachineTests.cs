using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Machines: API key machines with New machine's presets and scope rules per role, the one-time credentials, Reset,
// Delete with the typed id, the machine detail and the annotation restriction (in a disposable project), and the
// limits of a Contributor and a Reader. What a test creates and does not delete itself, its cleanup deletes.
[Area(Area.Access)]
public sealed class MachineTests : PlatformTest
{
    // Every scope of New machine, in its order: the runtime scopes, then the management scopes.
    private static readonly string[] AllScopes =
    [
        "Annotation.Read", "Annotation.Write", "Configuration.Read", "Configuration.Write", "Secrets.Read", "Secrets.Write",
        "Configuration.Transfer", "Schema.Write", "Template.Write", "Annotation.Manage", "KeyValue.Manage",
    ];

    // The machines a test created (in the project of its routes), deleted in CleanUpAsync while listed.
    private readonly List<(AppRoutes Routes, string Id)> createdMachines = [];

    // ACC-MCH-01: Create an API key machine, store its one-time credentials and delete it.
    // New machine starts from Runtime reader; Create shows the id and the API key once, in a dialog that neither Escape
    // nor a click outside closes; Copy copies the key with the secret's toast, the snippets carry it, the row lists the
    // machine, and Delete with the typed id removes it.
    [Test]
    [TestCase(132)]
    [UserRole(Role.Editor)]
    public async Task Machines_CreateApiKey_ShowsCredentialsOnce()
    {
        await InstallClipboardRecorderAsync();

        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await Expect(create.Scope("Annotation.Read")).ToBeCheckedAsync();
        await Expect(create.Scope("Configuration.Read")).ToBeCheckedAsync();

        await create.PickCredentialKindAsync("ApiKey");
        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        createdMachines.Add((Routes, id));
        await Expect(secret.Root).ToHaveAccessibleDescriptionAsync(OneTimeSecretDialog.DescriptionText);
        await Expect(secret.ShownOnlyOnce).ToBeVisibleAsync();
        await Expect(secret.DiscardHint).ToHaveTextAsync("Closing this dialog discards them; a reset issues new ones.");
        var key = await secret.ApiKeyAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Page.Mouse.ClickAsync(5, 5);
        await Expect(secret.Root).ToBeVisibleAsync();
        await Expect(secret.CloseButton).ToHaveCountAsync(0);
        await Expect(secret.DoneButton).ToBeDisabledAsync();

        await secret.CopyApiKeyButton.ClickAsync();
        await Expect(App.Toast("The clipboard contains a secret")).ToBeVisibleAsync();
        await Expect(App.Notifications.GetByText("Paste it where it belongs, then clear the clipboard.", new() { Exact = true }).Last).ToBeVisibleAsync();
        await WaitForCopiedTextAsync(key);

        await secret.SnippetsSummary.ClickAsync();
        await Expect(secret.Snippet("curl")).ToContainTextAsync($"Authorization: ApiKey {key}");
        await Expect(secret.Snippet(".env")).ToContainTextAsync($"STORYTELLER_API_KEY={key}");

        await secret.AcknowledgeAsync();
        await Expect(machines.ScopeBadges(id)).ToHaveTextAsync(["Annotation.Read", "Configuration.Read"]);
        await Expect(machines.MachineCell(id, "Annotation")).ToHaveTextAsync("any");
        await Expect(machines.MachineCell(id, "Credentials")).ToHaveTextAsync("API key");
        await Expect(machines.ResetButton(id)).ToBeVisibleAsync();
        await Expect(machines.DeleteButton(id)).ToBeVisibleAsync();

        var delete = machines.DeleteDialog(id);
        await machines.DeleteButton(id).ClickAsync();
        await Expect(delete.Root.GetByRole(AriaRole.Textbox, new() { Name = $"Type {id} to confirm", Exact = true })).ToBeVisibleAsync();
        await delete.TypeConfirmationAsync(id);
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast($"Machine {id} deleted")).ToBeVisibleAsync();
        await Expect(machines.MachineRow(id)).ToHaveCountAsync(0);
        createdMachines.Remove((Routes, id));
    }

    // ACC-MCH-02: Reset replaces the API key, and Delete needs the exact id.
    // Reset asks first and then shows a new API key once, without a certificate; Delete stays disabled until the
    // typed text is exactly the id, Cancel keeps the machine, and the exact id deletes it.
    [Test]
    [TestCase(135)]
    [UserRole(Role.Editor)]
    public async Task Machines_ResetApiKey_ShowsNewKey()
    {
        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();
        var (id, key) = await machines.CreateApiKeyMachineAsync();
        createdMachines.Add((Routes, id));

        var reset = machines.ResetDialog(id);
        await machines.ResetButton(id).ClickAsync();
        await reset.ExpectOpenAsync();
        await Expect(reset.Root).ToHaveAccessibleDescriptionAsync("The current secret of this machine stops working at once; a certificate keeps working. The new secret is shown once.");
        await reset.ConfirmAsync("Reset credentials");

        var renewed = new OneTimeSecretDialog(App, OneTimeSecretDialog.ResetTitle);
        await Expect(renewed.Root).ToBeVisibleAsync();
        await Assert.That(await renewed.ApiKeyAsync()).IsNotEqualTo(key);
        await Expect(renewed.DownloadPfxButton).ToHaveCountAsync(0);
        await renewed.AcknowledgeAsync();
        await Expect(machines.MachineCell(id, "Credentials")).ToHaveTextAsync("API key");

        var delete = machines.DeleteDialog(id);
        await machines.DeleteButton(id).ClickAsync();
        await delete.ExpectOpenAsync();
        await Expect(delete.Root).ToHaveAccessibleDescriptionAsync("The machine loses access immediately.");
        await Expect(delete.Button("Delete")).ToBeDisabledAsync();
        await delete.TypeConfirmationAsync(id[..^1]);
        await Expect(delete.Button("Delete")).ToBeDisabledAsync();
        await delete.CancelAsync();
        await Expect(machines.MachineRow(id)).ToBeVisibleAsync();

        await machines.DeleteButton(id).ClickAsync();
        await delete.TypeConfirmationAsync(id);
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast($"Machine {id} deleted")).ToBeVisibleAsync();
        await Expect(machines.MachineRow(id)).ToHaveCountAsync(0);
        createdMachines.Remove((Routes, id));
    }

    // ACC-MCH-03: Presets and scope rules of New machine.
    // An Administrator's presets replace the scopes (Git sync with its management scopes, but not KeyValue.Manage); a
    // write scope brings its read scope and loses it with it; without a runtime scope Create is disabled; every open
    // starts from Runtime reader again. Creates nothing.
    [Test]
    [TestCase(139)]
    [UserRole(Role.Admin)]
    public async Task Machines_Presets_ReplaceScopes()
    {
        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await Expect(create.Presets).ToBeVisibleAsync();
        await Expect(create.PresetsHint).ToBeVisibleAsync();
        await ExpectScopesAsync(create, "Annotation.Read", "Configuration.Read");

        await create.Preset("CI writer").ClickAsync();
        await ExpectScopesAsync(create, "Annotation.Read", "Annotation.Write", "Configuration.Read", "Configuration.Write");

        await create.Preset("Git sync").ClickAsync();
        await ExpectScopesAsync(
            create,
            "Annotation.Read",
            "Annotation.Write",
            "Configuration.Read",
            "Configuration.Write",
            "Configuration.Transfer",
            "Schema.Write",
            "Template.Write",
            "Annotation.Manage");

        await create.Preset("Runtime reader").ClickAsync();
        await create.Scope("Annotation.Write").CheckAsync();
        await ExpectScopesAsync(create, "Annotation.Read", "Annotation.Write", "Configuration.Read");

        await create.Scope("Annotation.Read").UncheckAsync();
        await ExpectScopesAsync(create, "Configuration.Read");

        await create.Scope("Configuration.Read").UncheckAsync();
        await Expect(create.NoRuntimeScope).ToBeVisibleAsync();
        await Expect(create.CreateButton).ToBeDisabledAsync();

        await create.CancelButton.ClickAsync();
        await Expect(create.Root).ToBeHiddenAsync();
        await machines.OpenCreateDialogAsync();
        await ExpectScopesAsync(create, "Annotation.Read", "Configuration.Read");
    }

    // ACC-MCH-04: A machine with management and secrets scopes, and its detail panel.
    // Backup plus Secrets.Write (which brings Secrets.Read) gives runtime, secrets and management scopes; the detail
    // describes them, opens beside the list from 768 px (full screen on a phone) and closes back to the list; an unknown
    // machine's detail shows Not found.
    [Test]
    [TestCase(141)]
    [UserRole(Role.Admin)]
    public async Task Machines_ManagementAndSecretsScopes_ShowOnDetail()
    {
        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await create.Preset("Backup").ClickAsync();
        await ExpectScopesAsync(create, "Annotation.Read", "Configuration.Read", "Configuration.Transfer");

        await create.Scope("Secrets.Write").CheckAsync();
        await Expect(create.Scope("Secrets.Read")).ToBeCheckedAsync();

        await create.PickCredentialKindAsync("ApiKey");
        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        createdMachines.Add((Routes, id));
        await secret.AcknowledgeAsync();
        await Expect(machines.ScopeBadges(id)).ToHaveTextAsync(["Annotation.Read", "Configuration.Read", "Secrets.Read", "Secrets.Write", "Configuration.Transfer"]);

        var detail = await machines.OpenMachineAsync(id);
        await Expect(detail.Value("Runtime scopes").Locator("[data-slot='label']")).ToHaveTextAsync(["Annotation.Read", "Configuration.Read", "Secrets.Read", "Secrets.Write"]);
        await Expect(detail.ScopeDescription("Runtime scopes", "Secrets.Write")).ToHaveTextAsync("Writes key/value secrets. Needs Configuration.Write as well.");
        await Expect(detail.Value("Management scopes").Locator("[data-slot='label']")).ToHaveTextAsync(["Configuration.Transfer"]);
        await Expect(detail.Value("Annotation")).ToHaveTextAsync("any annotation");
        await Expect(detail.Value("Credentials")).ToHaveTextAsync("API key");

        // The detail's mode follows the viewport: the list stays beside the overlay from 768 px.
        var width = Page.ViewportSize?.Width ?? int.MaxValue;
        if (width >= DetailPanel.OverlayWidth)
        {
            await detail.Panel.ExpectModeAsync(DetailPanel.OverlayMode);
            await Expect(machines.MachinesTable).ToBeVisibleAsync();
            await Expect(detail.Panel.FullScreenButton).ToBeVisibleAsync();
            await Expect(detail.Panel.PinButton).ToHaveCountAsync(width >= DetailPanel.PinnedWidth ? 1 : 0);
        }
        else
        {
            await detail.Panel.ExpectModeAsync(DetailPanel.FullMode);
            await Expect(machines.MachinesTable).ToBeHiddenAsync();
            await Expect(detail.Panel.FullScreenButton).ToHaveCountAsync(0);
            await Expect(detail.Panel.PinButton).ToHaveCountAsync(0);
        }

        await detail.CloseAsync();
        await Expect(machines.MachinesTable).ToBeVisibleAsync();

        // A GUID: a deployed target may refuse another id with 400 instead of 404.
        var unknown = machines.Detail(Guid.NewGuid().ToString());
        await unknown.OpenAsync();
        await Expect(unknown.NotFound).ToBeVisibleAsync();
        await Expect(unknown.RetryButton).ToBeVisibleAsync();
    }

    // ACC-MCH-05: A Contributor gives neither management nor secrets scopes.
    // A Contributor's Machines page has New machine but no Administrator sections; New machine disables the secrets and
    // management scopes and the presets with them, and a CI writer machine is created with Reset and Delete.
    [Test]
    [TestCase(160)]
    [UserRole(Role.Contributor)]
    public async Task Machines_Contributor_GivesRuntimeScopesOnly()
    {
        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();
        await Expect(machines.NewMachineButton).ToBeVisibleAsync();
        await Expect(machines.SharedCertificates.Root).ToHaveCountAsync(0);
        await Expect(machines.TrustedIssuers.Root).ToHaveCountAsync(0);

        var create = await machines.OpenCreateDialogAsync();
        await Expect(create.ManagementScopesHelp).ToBeVisibleAsync();
        await Expect(create.SecretsScopesHelp).ToBeVisibleAsync();
        foreach (var scope in new[] { "Secrets.Read", "Secrets.Write", "Configuration.Transfer", "Schema.Write", "Template.Write", "Annotation.Manage", "KeyValue.Manage" })
        {
            await Expect(create.Scope(scope)).ToBeDisabledAsync();
        }

        foreach (var scope in new[] { "Annotation.Read", "Annotation.Write", "Configuration.Read", "Configuration.Write" })
        {
            await Expect(create.Scope(scope)).ToBeEnabledAsync();
        }

        foreach (var preset in new[] { "Runtime with secrets", "Git sync", "Backup", "Schema deployer" })
        {
            await Expect(create.Preset(preset)).ToBeDisabledAsync();
        }

        await Expect(create.Preset("Runtime reader")).ToBeEnabledAsync();
        await Expect(create.Preset("CI writer")).ToBeEnabledAsync();

        await create.Preset("CI writer").ClickAsync();
        await create.PickCredentialKindAsync("ApiKey");
        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        createdMachines.Add((Routes, id));
        await secret.AcknowledgeAsync();
        await Expect(machines.ScopeBadges(id)).ToHaveTextAsync(["Annotation.Read", "Annotation.Write", "Configuration.Read", "Configuration.Write"]);
        await Expect(machines.ResetButton(id)).ToBeVisibleAsync();
        await Expect(machines.DeleteButton(id)).ToBeVisibleAsync();
    }

    // ACC-MCH-06: A Contributor can neither reset nor delete machines with elevated scopes.
    // In the mock's acme/shipping, machines an Owner added with a management or a secrets scope have neither Reset nor
    // Delete for the Contributor, and the Federated one has Setup but no Edit rules; the runtime-only machine has both,
    // and its Delete works.
    [Test]
    [TestCase(161)]
    [UserRole(Role.Contributor)]
    [MockOnly("machines with elevated scopes, added to acme/shipping through window.storytellerMock.addMachine as its Owner would")]
    public async Task Machines_Contributor_CannotResetElevatedMachines()
    {
        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();

        var (runtime, transfer, secrets, federated) = (PlatformData.MachineName(), PlatformData.MachineName(), PlatformData.MachineName(), PlatformData.MachineName());
        var origin = new Uri(Page.Url).GetLeftPart(UriPartial.Authority);
        await MockApi.AddMachineAsync(new { Id = runtime, ObjectId = runtime, Scopes = new[] { "AnnotationRead", "ConfigurationRead" }, CredentialKind = "ApiKey" });
        await MockApi.AddMachineAsync(new { Id = transfer, ObjectId = transfer, Scopes = new[] { "AnnotationRead", "ConfigurationRead", "ConfigurationTransfer" }, CredentialKind = "ApiKey" });
        await MockApi.AddMachineAsync(new { Id = secrets, ObjectId = secrets, Scopes = new[] { "ConfigurationRead", "SecretsRead" }, CredentialKind = "ApiKey" });
        await MockApi.AddMachineAsync(new
        {
            Id = federated,
            ObjectId = federated,
            AccessKey = string.Empty,
            Scopes = new[] { "AnnotationRead", "ConfigurationRead", "ConfigurationTransfer" },
            CredentialKind = "Federated",
            FederationRules = new[]
            {
                new { Issuer = "github", Audience = $"{origin}/api/v1/{Routes.Organization}/{Routes.Project}/access/token", Subject = PlatformData.GitHubSubject() },
            },
        });
        await machines.OpenAsync();

        await Expect(machines.ResetButton(runtime)).ToBeVisibleAsync();
        await Expect(machines.DeleteButton(runtime)).ToBeVisibleAsync();

        await Expect(machines.MachineRow(transfer)).ToContainTextAsync("Configuration.Transfer");
        await Expect(machines.ResetButton(transfer)).ToHaveCountAsync(0);
        await Expect(machines.DeleteButton(transfer)).ToHaveCountAsync(0);

        await Expect(machines.MachineRow(secrets)).ToContainTextAsync("Secrets.Read");
        await Expect(machines.ResetButton(secrets)).ToHaveCountAsync(0);
        await Expect(machines.DeleteButton(secrets)).ToHaveCountAsync(0);

        var detail = await machines.OpenMachineAsync(federated);
        await Expect(detail.SetupButton).ToBeVisibleAsync();
        await Expect(detail.EditRulesButton).ToHaveCountAsync(0);

        await detail.CloseAsync();
        await machines.DeleteMachineAsync(runtime);
        await Expect(machines.MachineRow(transfer)).ToHaveCountAsync(1);
    }

    // ACC-MCH-07: A machine restricted to an annotation.
    // In a disposable project, a responsibility created with Bulk create in the default view is the only option of New
    // machine's "Annotation key"; the machine created with it shows the key in its row and on its detail.
    [Test]
    [TestCase(162)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Machines_AnnotationRestriction_ShowsKey()
    {
        var project = await CreateDisposableProjectAsync();
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);

        var bulk = await new AnnotationsPage(App, project).OpenBulkCreateAsync();
        await bulk.EnterKeysAsync(key);
        await bulk.CreateAsync(1);
        await Expect(bulk.Result).ToHaveTextAsync("Created 1 annotation.");
        await Expect(bulk.CreatedRow(key)).ToContainTextAsync("From the list");
        await bulk.CloseAsync();

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await create.AnnotationKey.ClickAsync();
        await create.AnnotationKey.PressSequentiallyAsync(key[(key.IndexOf('.') + 1)..]);
        await Expect(create.AnnotationKeyOptions).ToHaveCountAsync(1);
        await Expect(create.AnnotationKeyOptions).ToHaveTextAsync(key);
        await Expect(create.AnnotationKeyHelp).ToBeVisibleAsync();

        await create.AnnotationKeyOptions.ClickAsync();
        await Expect(create.AnnotationKey).ToHaveValueAsync(key);
        await create.PickCredentialKindAsync("ApiKey");
        var secret = await create.CreateAsync();
        var id = await secret.IdAsync();
        createdMachines.Add((project, id));
        await secret.AcknowledgeAsync();
        await Expect(machines.MachineCell(id, "Annotation")).ToHaveTextAsync(key);

        var detail = await machines.OpenMachineAsync(id);
        await Expect(detail.Value("Annotation")).ToHaveTextAsync(key);
    }

    // ACC-MCH-08: A Reader has no access to Machines.
    // A Reader's Machines page offers no New machine, its list shows No access with Retry, and the Administrator
    // sections are not there.
    [Test]
    [TestCase(163)]
    [UserRole(Role.Reader)]
    public async Task Machines_Reader_ShowsNoAccess()
    {
        var machines = new MachinesPage(App, Routes);
        await machines.OpenAsync();
        await Expect(machines.NoAccess).ToBeVisibleAsync();
        await Expect(machines.RetryButton).ToBeVisibleAsync();
        await Expect(machines.NewMachineButton).ToHaveCountAsync(0);
        await Expect(machines.SharedCertificates.Root).ToHaveCountAsync(0);
        await Expect(machines.TrustedIssuers.Root).ToHaveCountAsync(0);
    }

    // Deletes the machines the test created and did not delete itself, project by project.
    protected override async Task CleanUpAsync()
    {
        foreach (var project in createdMachines.GroupBy(machine => machine.Routes))
        {
            var machines = new MachinesPage(App, project.Key);
            await machines.OpenAsync();
            foreach (var (_, id) in project)
            {
                await machines.DeleteMachineIfListedAsync(id);
            }
        }
    }

    // Checks exactly the scopes of New machine that are checked; every other scope is not.
    private static async Task ExpectScopesAsync(MachineCreateDialog dialog, params string[] checkedScopes)
    {
        foreach (var scope in AllScopes)
        {
            if (checkedScopes.Contains(scope, StringComparer.Ordinal))
            {
                await Expect(dialog.Scope(scope)).ToBeCheckedAsync();
            }
            else
            {
                await Expect(dialog.Scope(scope)).Not.ToBeCheckedAsync();
            }
        }
    }
}
