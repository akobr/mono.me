using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Modeling;

// Tabs and Access: the tab shell of Modeling (path tabs, manual keyboard activation, browser Back), the redirects of the
// old URLs of the merged pages, the locks of a Reader and of a Contributor, the remembered tab and the Discard changes?
// guard of a dirty editor.
[Area(Area.Modeling)]
public sealed class ModelingTabsTests : PlatformTest
{
    // MOD-TAB-01: Modeling opens on Schemas and every tab opens its own page.
    // The plain …/modeling opens Schemas, and an Administrator gets the five tabs in their order. Each click opens its
    // tab's page, browser Back returns to the tab before, and the arrow keys only move the focus until Enter opens the
    // focused tab (manual activation), which keeps the focus. Compare views is not clicked: a project with one view
    // disables it (MOD-CMP-01).
    [Test]
    [TestCase(14)]
    [UserRole(Role.Admin)]
    public async Task Modeling_OpenTabs_EachTabOpensItsPage()
    {
        var modeling = new ModelingPage(App, Routes);
        var schemas = new ModelingSchemaPage(App, Routes);
        var exportImport = new ModelingExportImportPage(App, Routes);
        var deleted = new ModelingDeletedPage(App, Routes);

        await App.GotoAsync(Routes.Modeling);
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await Expect(schemas.SchemaListSection("Type schemas")).ToBeVisibleAsync();
        await modeling.ExpectSelectedAsync("Schemas");
        await Expect(modeling.HeaderTabs).ToHaveTextAsync(["Schemas", "Templates", "Export & Import", "Deleted", "Compare views"]);

        await modeling.SelectTabAsync("Templates");
        await App.ExpectAtAsync(Routes.ModelingTab("templates"));

        await modeling.SelectTabAsync("Export & Import");
        await App.ExpectAtAsync(Routes.ModelingTab("export-import"));
        await Expect(exportImport.ExportRegion).ToBeVisibleAsync();
        await Expect(exportImport.ImportRegion).ToBeVisibleAsync();

        await modeling.SelectTabAsync("Deleted");
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await Expect(deleted.ShowCascaded).ToBeVisibleAsync();

        await modeling.SelectTabAsync("Schemas");
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await Page.GoBackAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await modeling.ExpectSelectedAsync("Deleted");

        await modeling.Tab("Deleted").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(modeling.Tab("Export & Import")).ToBeFocusedAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await modeling.ExpectSelectedAsync("Deleted");

        await Page.Keyboard.PressAsync("Enter");
        await App.ExpectAtAsync(Routes.ModelingTab("export-import"));
        await modeling.ExpectSelectedAsync("Export & Import");
        await Expect(modeling.Tab("Export & Import")).ToBeFocusedAsync();
    }

    // MOD-TAB-02: Old URLs of the merged pages redirect to their Modeling tabs.
    // Every old page of a view (…/schemas, /templates, /export-import, /deleted and /compare) opens its tab of Modeling
    // with the rest of the path and the query, each by a fresh page load; the export and the import show their tab to
    // an Administrator and their lock to a Contributor. A template of a type code the app does not know says so.
    [Test]
    [TestCase(40)]
    [UserRole(Role.Editor)]
    public async Task Modeling_OldUrls_RedirectToModelingTabs()
    {
        var modeling = new ModelingPage(App, Routes);
        var schemas = new ModelingSchemaPage(App, Routes);
        var templates = new ModelingTemplatePage(App, Routes);
        var deleted = new ModelingDeletedPage(App, Routes);
        var exportImport = new ModelingExportImportPage(App, Routes);

        await App.GotoAsync(Routes.OldViewPage("schemas"));
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await modeling.ExpectSelectedAsync("Schemas");
        await Expect(schemas.SchemaListSection("Type schemas")).ToBeVisibleAsync();

        // A detail is full screen on a phone and hides the navbar and the tabs, so these two wait for the detail only.
        await App.GotoAsync(Routes.OldViewPage("templates", "rst"));
        await App.ExpectAtAsync(Routes.Template("rst"));
        await Expect(templates.Panel.Root).ToBeVisibleAsync();

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldViewPage("schemas", "type/rst"), ("tab", "versions")));
        await App.ExpectAtAsync(Routes.TypeSchema("rst"));
        await App.ExpectQueryAsync("tab", "versions");
        await Expect(schemas.Panel.Root).ToBeVisibleAsync();
        await Expect(schemas.VersionsTab).ToHaveAttributeAsync("aria-selected", "true");

        await App.GotoAsync(Routes.OldViewPage("deleted"));
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await App.ExpectQueryAsync("cascaded", null);
        await modeling.ExpectSelectedAsync("Deleted");
        await Expect(deleted.ShowCascaded).Not.ToBeCheckedAsync();

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldViewPage("deleted"), ("cascaded", "1")));
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await App.ExpectQueryAsync("cascaded", "1");
        await Expect(deleted.ShowCascaded).ToBeCheckedAsync();

        await App.GotoAsync(Routes.OldViewPage("export-import"));
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("export-import"));

        // The leased editor's roles in Users:Pool: an Administrator or Owner sees Export & Import.
        if (AdminUser.IsSatisfiedBy(User))
        {
            await modeling.ExpectSelectedAsync("Export & Import");
        }
        else
        {
            await Expect(exportImport.AdministratorsOnly).ToBeVisibleAsync();
        }

        // Selected also where it is disabled: a project with one view.
        await App.GotoAsync(Routes.OldViewPage("compare"));
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("compare"));
        await modeling.ExpectSelectedAsync("Compare views");

        await App.GotoAsync(Routes.Template("foo"));
        await Expect(templates.UnknownType).ToBeVisibleAsync();
    }

    // MOD-TAB-03: A Reader gets the Contributors only lock on every Modeling URL.
    // Below Contributor the whole of Modeling is one lock: every tab's URL, also a template's detail, shows "Contributors
    // only" with its description and mounts nothing below it, so there are no tabs, no New schema, no detail, no Export
    // or Import region, no Show cascaded and no key picker of Compare views.
    [Test]
    [TestCase(42)]
    [UserRole(Role.Reader)]
    public async Task Modeling_Reader_GetsContributorsOnlyLock()
    {
        var modeling = new ModelingPage(App, Routes);
        var schemas = new ModelingSchemaPage(App, Routes);
        var templates = new ModelingTemplatePage(App, Routes);
        var exportImport = new ModelingExportImportPage(App, Routes);
        var deleted = new ModelingDeletedPage(App, Routes);
        var compare = new ModelingComparePage(App, Routes);

        await App.GotoAsync(Routes.Modeling);
        await App.WaitForReadyAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await ExpectContributorsOnlyAsync(modeling);
        await Expect(schemas.NewSchemaButton).ToHaveCountAsync(0);

        await App.GotoAsync(Routes.Template("exe"));
        await App.WaitForReadyAsync();
        await ExpectContributorsOnlyAsync(modeling);
        await Expect(templates.Panel.Root).ToHaveCountAsync(0);

        await exportImport.OpenAsync();
        await ExpectContributorsOnlyAsync(modeling);
        await Expect(exportImport.ExportRegion).ToHaveCountAsync(0);
        await Expect(exportImport.ImportRegion).ToHaveCountAsync(0);

        await App.GotoAsync(Routes.Deleted());
        await App.WaitForReadyAsync();
        await ExpectContributorsOnlyAsync(modeling);
        await Expect(deleted.ShowCascaded).ToHaveCountAsync(0);

        await compare.OpenAsync();
        await ExpectContributorsOnlyAsync(modeling);
        await Expect(compare.KeyPicker).ToHaveCountAsync(0);
    }

    // MOD-TAB-04: A Contributor gets four tabs, the Administrators only lock and no Delete permanently.
    // A Contributor's header has no Export & Import, and its URL shows the lock "Administrators only" under the four
    // other tabs with none selected. On Deleted, the subject the test deleted offers the restores but not Delete
    // permanently, in its row menu and in the bulk toolbar of a selection; the bulk Restore brings it back.
    [Test]
    [TestCase(43)]
    [UserRole(Role.Contributor)]
    public async Task Modeling_Contributor_GetsNoAdministratorFeatures()
    {
        var subject = PlatformData.AnnotationKey(PlatformData.Subject);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(subject);
        var detail = new AnnotationDetail(App, Routes);
        await detail.OpenAsync(subject);
        await detail.DeleteAsync(subject);

        var modeling = new ModelingPage(App, Routes);
        var schemas = new ModelingSchemaPage(App, Routes);
        await modeling.OpenAsync("schemas");
        await Expect(modeling.HeaderTabs).ToHaveTextAsync(["Schemas", "Templates", "Deleted", "Compare views"]);
        await modeling.ExpectSelectedAsync("Schemas");
        await Expect(schemas.NewSchemaButton).ToBeVisibleAsync();

        var exportImport = new ModelingExportImportPage(App, Routes);
        await exportImport.OpenAsync();
        await Expect(exportImport.AdministratorsOnly).ToBeVisibleAsync();
        await Expect(exportImport.AdministratorsOnlyDescription).ToBeVisibleAsync();
        await Expect(modeling.HeaderTabs).ToHaveTextAsync(["Schemas", "Templates", "Deleted", "Compare views"]);
        await Expect(modeling.SelectedTabs).ToHaveCountAsync(0);
        await Expect(exportImport.ExportRegion).ToHaveCountAsync(0);
        await Expect(exportImport.ImportRegion).ToHaveCountAsync(0);

        var deleted = new ModelingDeletedPage(App, Routes);
        await modeling.SelectTabAsync("Deleted");
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await Expect(deleted.Row(subject)).ToBeVisibleAsync();

        await deleted.OpenActionsAsync(subject);
        await Expect(deleted.MenuItems).ToHaveTextAsync(["Show details", "Restore", "Restore without descendants"]);
        await Expect(deleted.MenuItem("Delete permanently")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(deleted.MenuItems).ToHaveCountAsync(0);
        await deleted.SelectCheckbox(subject).CheckAsync();
        await Expect(deleted.BulkActions).ToContainTextAsync("1 selected");
        await Expect(deleted.BulkRestoreButton).ToBeVisibleAsync();
        await Expect(deleted.BulkDeletePermanentlyButton).ToHaveCountAsync(0);

        await deleted.BulkRestoreButton.ClickAsync();
        await Expect(App.Toast($"{subject} restored")).ToBeVisibleAsync();
        await Expect(deleted.Row(subject)).ToHaveCountAsync(0);
    }

    // MOD-TAB-05: Modeling returns to the last tab, detail and query after another section.
    // The sidebar's Modeling brings back the last tab with its query (Deleted with Show cascaded) after another section
    // and, on a desktop, the last open detail (the Responsibility template, only read); Modeling again while on Modeling
    // opens Schemas. The memory lives in the page, so after the first page load every step navigates by clicks. Below
    // 1024 px the detail covers the tabs or the navigation, so the detail's steps are left out there.
    [Test]
    [TestCase(44)]
    [UserRole(Role.Editor)]
    public async Task Modeling_ReturnFromOtherSection_RestoresLastTab()
    {
        var modeling = new ModelingPage(App, Routes);
        var deleted = new ModelingDeletedPage(App, Routes);
        var templates = new ModelingTemplatePage(App, Routes);

        await deleted.OpenAsync();
        await deleted.ShowCascaded.ClickAsync();
        await App.ExpectQueryAsync("cascaded", "1");
        await Expect(deleted.ShowCascaded).ToBeCheckedAsync();

        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        await Expect(new AnnotationsPage(App, Routes).Filter).ToBeVisibleAsync();

        await App.NavigateAsync("Modeling");
        await App.ExpectAtAsync(Routes.ModelingTab("deleted"));
        await App.ExpectQueryAsync("cascaded", "1");
        await modeling.ExpectSelectedAsync("Deleted");
        await Expect(deleted.ShowCascaded).ToBeCheckedAsync();

        if (App.HasDockedSidebar)
        {
            await modeling.SelectTabAsync("Templates");
            await templates.ListRow("rst").ClickAsync();
            await App.ExpectAtAsync(Routes.Template("rst"));
            await Expect(templates.Panel.Root).ToBeVisibleAsync();

            await App.NavigateAsync("Configurations");
            await App.ExpectAtAsync(Routes.Configurations);
            await App.NavigateAsync("Modeling");
            await App.ExpectAtAsync(Routes.Template("rst"));
            await Expect(templates.Panel.Root).ToBeVisibleAsync();
            await modeling.ExpectSelectedAsync("Templates");
        }

        await App.NavigateAsync("Modeling");
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await modeling.ExpectSelectedAsync("Schemas");
    }

    // MOD-TAB-06: A dirty schema editor asks Discard changes? before another tab opens.
    // After its first save (v1) the schema of a new responsibility is clean; a change marks the page title with "● " and
    // a click on another tab asks "Discard changes?". Keep editing stays with the draft, Discard opens the tab and drops
    // it, so the schema still holds v1. Desktop only: below 1024 px the detail covers the tabs.
    [Test]
    [TestCase(46)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task Modeling_LeaveDirtySchemaEditor_AsksToDiscard()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var modeling = new ModelingPage(App, Routes);
        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenAnnotationAsync(key);
        await schema.CreateAsync("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        await Expect(schema.SaveButton).ToBeDisabledAsync();
        await Expect(Page).Not.ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());

        await schema.ReplaceInDocumentAsync("\"string\"", "\"number\"");
        await Expect(Page).ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());
        await Expect(schema.SaveButton).ToBeEnabledAsync();

        var discard = new DiscardChangesDialog(App);
        await modeling.Tab("Templates").ClickAsync();
        await discard.ExpectOpenAsync();
        await Expect(discard.Description).ToHaveTextAsync(DiscardChangesDialog.DescriptionText);

        await discard.KeepEditingAsync();
        await App.ExpectAtAsync(Routes.AnnotationSchema(key));
        await modeling.ExpectSelectedAsync("Schemas");
        await Expect(schema.Document.Content).ToContainTextAsync("\"number\"");

        await modeling.Tab("Templates").ClickAsync();
        await discard.DiscardAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("templates"));
        await modeling.ExpectSelectedAsync("Templates");
        await Expect(Page).Not.ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());

        await schema.OpenAnnotationAsync(key);
        await Expect(schema.Document.Content).ToContainTextAsync("\"string\"");
        await Expect(schema.Document.Content).Not.ToContainTextAsync("\"number\"");
    }

    private static async Task ExpectContributorsOnlyAsync(ModelingPage modeling)
    {
        await Expect(modeling.ContributorsOnly).ToBeVisibleAsync();
        await Expect(modeling.ContributorsOnlyDescription).ToBeVisibleAsync();
        await Expect(modeling.AllTabs).ToHaveCountAsync(0);
    }
}
