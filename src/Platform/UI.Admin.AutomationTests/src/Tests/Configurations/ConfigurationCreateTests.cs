using System.Text.Json.Nodes;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Create and Delete: the first own configuration of an annotation (Create empty, Start from template, the New
// configuration dialog of the list) and Delete own with the typed key.
[Area(Area.Configurations)]
public sealed class ConfigurationCreateTests : PlatformTest
{
    // CFG-CRE-01: Create empty, save a JSON document, and the Effective tab shows it.
    // A new responsibility (Bulk create) gets an empty configuration (v1, {}), then a generated document in the JSON
    // editor (3 changes); after the save (v2) the Effective tab shows every value of the document. A responsibility has
    // no ancestors and, usually, no type schema or template, so the test depends on none of the target's modeling.
    [Test]
    [Elementary]
    [TestCase(3)]
    [UserRole(Role.Editor)]
    public async Task Configurations_SetDocument_EffectiveShowsIt()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var document = PlatformData.ConfigurationDocument();

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();
        await Expect(configuration.CreateEmptyButton).ToBeVisibleAsync();
        await Expect(configuration.StartFromTemplateButton).ToBeVisibleAsync();

        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await configuration.Document.ExpectTextAsync("{}");
        await Expect(configuration.VersionBadge(1)).ToBeVisibleAsync();

        await configuration.SetDocumentAsync(document);
        await Expect(configuration.Changes(3)).ToBeVisibleAsync();
        await Expect(configuration.SaveButton).ToBeEnabledAsync();

        await configuration.SaveAsync(2);
        await Expect(configuration.VersionBadge(2)).ToBeVisibleAsync();
        await Expect(configuration.ChangeCounter).ToHaveCountAsync(0);

        var effective = await configuration.OpenEffectiveAsync();
        foreach (var (name, value) in JsonNode.Parse(document)!.AsObject())
        {
            await Expect(effective).ToContainTextAsync($"\"{name}\": {value!.ToJsonString()}");
        }
    }

    // CFG-CRE-02: New configuration offers only annotations without their own configuration.
    // A new responsibility has no configuration, so the filtered list is empty and its empty state's New configuration
    // offers the key; Open editor (by Enter, the focus moved to it) opens its empty configuration with the filter kept.
    // Once Create empty stored v1, the list shows the row with v1 and the dialog no longer offers the key.
    [Test]
    [TestCase(63)]
    [UserRole(Role.Editor)]
    public async Task Configurations_NewDialog_OffersOnlyKeysWithoutConfiguration()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var filter = $"key:{key}";

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var list = new ConfigurationsPage(App, Routes);
        await list.OpenFilteredAsync(filter);
        await Expect(list.NoMatchHeading).ToBeVisibleAsync();
        await Expect(list.EmptyStateDescription).ToBeVisibleAsync();
        await Expect(list.NewConfigurationButtons).ToHaveCountAsync(2);

        var dialog = await list.OpenNewConfigurationAsync(list.EmptyStateNewConfigurationButton);
        await Expect(dialog.Description).ToHaveTextAsync($"In {Routes.Organization}/{Routes.Project}, view {Routes.View}.");
        await Expect(dialog.OpenEditorButton).ToBeDisabledAsync();

        await dialog.SearchAsync(key);
        await Expect(dialog.Option(key)).ToBeVisibleAsync();
        await dialog.Option(key).ClickAsync();
        await Expect(dialog.AnnotationTrigger).ToHaveTextAsync(key);
        await Expect(dialog.OpenEditorButton).ToBeEnabledAsync();
        await Expect(dialog.OpenEditorButton).ToBeFocusedAsync();

        await App.Page.Keyboard.PressAsync("Enter");
        await Expect(dialog.Root).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.Configuration(key));
        await App.ExpectQueryAsync("q", filter);
        var configuration = new ConfigurationDetail(App, Routes);
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();

        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await Expect(configuration.VersionBadge(1)).ToBeVisibleAsync();

        await configuration.ConfigurationPanel.CloseAsync();
        await App.ExpectAtAsync(Routes.Configurations);
        await App.ExpectQueryAsync("q", filter);
        if (list.HasGridLayout)
        {
            await Expect(list.ItemCell(key, ConfigurationsPage.VersionColumn)).ToHaveTextAsync("v1");
        }
        else
        {
            await Expect(list.Item(key).GetByText("v1", new() { Exact = true })).ToBeVisibleAsync();
        }

        dialog = await list.OpenNewConfigurationAsync(list.NewConfigurationButton);
        await dialog.SearchAsync(key);
        await Expect(dialog.Option(key)).ToHaveCountAsync(0);

        // The search's list is open over the dialog: Escape closes it first, then Cancel closes the dialog.
        await App.Page.Keyboard.PressAsync("Escape");
        await Expect(dialog.Search).ToBeHiddenAsync();
        await dialog.CancelAsync();
    }

    // CFG-CRE-03: Start from template copies the type template, and Create empty still inherits it.
    // A type template changes every Responsibility configuration of its view, so the test sets it in a project of its
    // own: Start from template stores the template as A's own v1, while B's empty own configuration still shows every
    // value of the template in Effective (the template is merged before the own content).
    [Test]
    [TestCase(74)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Configurations_StartFromTemplate_CopiesTypeTemplate()
    {
        var project = await CreateDisposableProjectAsync();
        var template = PlatformData.TemplateDocument();
        var values = JsonNode.Parse(template)!.AsObject();
        var first = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var second = PlatformData.AnnotationKey(PlatformData.Responsibility);

        var templates = new ModelingTemplatePage(App, project);
        await templates.OpenAsync(PlatformData.Responsibility);
        await templates.CreateAsync(template);

        var annotations = new AnnotationsPage(App, project);
        await annotations.OpenAsync();
        var bulk = await annotations.OpenBulkCreateAsync();
        await bulk.EnterKeysAsync(first, second);
        await bulk.CreateAsync(2);
        await Expect(bulk.Result).ToHaveTextAsync("Created 2 annotations.");
        await bulk.CloseAsync();

        var configuration = new ConfigurationDetail(App, project);
        await configuration.OpenAsync(first);
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();
        await Expect(configuration.CreateEmptyButton).ToBeVisibleAsync();
        await Expect(configuration.StartFromTemplateButton).ToBeVisibleAsync();

        await configuration.StartFromTemplateAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await Expect(configuration.VersionBadge(1)).ToBeVisibleAsync();

        await configuration.JsonMode.ClickAsync();
        await App.ExpectQueryAsync("format", "json");
        foreach (var (name, value) in values)
        {
            await Expect(configuration.Editor).ToContainTextAsync($"\"{name}\": {value!.ToJsonString()}");
        }

        await configuration.OpenAsync(second);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await configuration.Document.ExpectTextAsync("{}");

        var effective = await configuration.OpenEffectiveAsync();
        foreach (var (name, value) in values)
        {
            await Expect(effective).ToContainTextAsync($"\"{name}\": {value!.ToJsonString()}");
        }
    }

    // CFG-CRE-04: Delete own needs the typed key and returns to the empty state.
    // Delete own asks for the key: Cancel keeps the configuration, a wrong text keeps Delete disabled, the key enables
    // it. The delete returns the Edit tab to "No configuration yet", and Close goes back to the filtered list, now
    // empty. Version numbers after the delete are not checked: the mock drops the history, a deployed API may keep it.
    [Test]
    [TestCase(81)]
    [UserRole(Role.Editor)]
    public async Task Configurations_DeleteOwn_NeedsTypedKey()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var filter = $"key:{key}";

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenFilteredAsync(key, filter);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await configuration.SetDocumentAsync(PlatformData.ConfigurationDocument());
        await configuration.SaveAsync(2);

        var confirmation = configuration.DeleteOwnDialog(key);
        await configuration.DeleteOwnButton.ClickAsync();
        await confirmation.ExpectOpenAsync();
        await Expect(confirmation.Description).ToHaveTextAsync("Only the own content is deleted; values inherited from ancestors and the template remain.");
        await Expect(confirmation.Button("Delete")).ToBeDisabledAsync();

        await confirmation.CancelAsync();
        await Expect(configuration.Editor).ToBeVisibleAsync();
        await Expect(configuration.VersionBadge(2)).ToBeVisibleAsync();

        await configuration.DeleteOwnButton.ClickAsync();
        await confirmation.ExpectOpenAsync();
        var typed = confirmation.Root.GetByRole(AriaRole.Textbox, new() { Name = $"Type {key} to confirm", Exact = true });
        await typed.FillAsync("x");
        await Expect(confirmation.Button("Delete")).ToBeDisabledAsync();

        await typed.FillAsync(key);
        await Expect(confirmation.Button("Delete")).ToBeEnabledAsync();

        await confirmation.ConfirmAsync("Delete");
        await Expect(App.Toast("Own configuration deleted; inherited values remain")).ToBeVisibleAsync();
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();
        await Expect(configuration.CreateEmptyButton).ToBeVisibleAsync();

        // Admin UI bug (424-152 review): ConfigurationEditor.vue remove() leaves the draft {} while nothing is stored, so
        // leaving after Delete own may ask "Discard changes?"; discard it only when it shows. The outcome is the detail
        // gone, not the list's empty state: from 768 px the list beside the detail shows it already after the delete.
        var list = new ConfigurationsPage(App, Routes);
        await configuration.ConfigurationPanel.LeaveButton.ClickAsync();
        await new DiscardChangesDialog(App).DiscardIfShownAsync(configuration.ConfigurationPanel.Gone);
        await Expect(configuration.ConfigurationPanel.Root).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.Configurations);
        await App.ExpectQueryAsync("q", filter);
        await Expect(list.NoMatchHeading).ToBeVisibleAsync();
    }
}
