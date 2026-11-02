using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Configurations section's part of the configuration detail (components/configuration/ConfigurationEditor.vue and
// the tabs of pages/[org]/[project]/[view]/configurations/[key].vue). Everything is scoped to the detail's region
// (section#configuration-detail, named by the key): on a desktop the list stays behind the overlay with its own
// "v<n>" cells. The Edit tab's toolbar has the radiogroup "Editing mode" (Form only when a schema applies), the
// version badge "v<n>" and "Delete own"; its sticky footer (Contributor and up) shows "<n> change(s)" while the draft
// differs (the JSON Patch operations without the guarding tests), "The form has errors." in Form mode, "Revert" and
// "Save". A 409 shows the alert "The configuration does not match its schema" with "Save anyway", a 412 the dialog
// "Someone else changed this configuration" (ConfigurationConflictDialog).
public sealed partial class ConfigurationDetail
{
    // The detail's region, named by the annotation key.
    public DetailPanel ConfigurationPanel => new(app, "configuration-detail");

    // The empty state's heading (an h2) of an annotation without an own configuration.
    public ILocator NoConfigurationHeading => ConfigurationPanel.Root.GetByRole(AriaRole.Heading, new() { Name = "No configuration yet", Exact = true });

    public ILocator StartFromTemplateButton => ConfigurationPanel.Root.GetByRole(AriaRole.Button, new() { Name = "Start from template", Exact = true });

    public ILocator EditingMode => ConfigurationPanel.Root.GetByRole(AriaRole.Radiogroup, new() { Name = "Editing mode", Exact = true }).First;

    // The radio "Form" exists only while a schema applies to the annotation.
    public ILocator FormMode => EditingMode.GetByRole(AriaRole.Radio, new() { Name = "Form", Exact = true });

    public ILocator YamlMode => EditingMode.GetByRole(AriaRole.Radio, new() { Name = "YAML", Exact = true });

    // The header's "Annotation" button, a link to the annotation (without the list's q); not the sidebar's
    // "Annotations".
    public ILocator AnnotationLink => ConfigurationPanel.Root.GetByRole(AriaRole.Link, new() { Name = "Annotation", Exact = true });

    public ILocator VersionsTab => ConfigurationPanel.Root.GetByRole(AriaRole.Link, new() { Name = "Versions", Exact = true });

    public ILocator SchemaTab => ConfigurationPanel.Root.GetByRole(AriaRole.Link, new() { Name = "Schema", Exact = true });

    public ILocator DeleteOwnButton => ConfigurationPanel.Root.GetByRole(AriaRole.Button, new() { Name = "Delete own", Exact = true });

    public ILocator RevertButton => ConfigurationPanel.Root.GetByRole(AriaRole.Button, new() { Name = "Revert", Exact = true });

    // "<n> change" or "<n> changes" in the footer, only while the draft differs from the saved own content.
    public ILocator ChangeCounter => ConfigurationPanel.Root.GetByText(ChangeCounterText());

    public ILocator FormErrors => ConfigurationPanel.Root.GetByText("The form has errors.", new() { Exact = true });

    // The parse error under the JSON or YAML editor, "Line <n>: <message>" (role alert).
    public ILocator ParseError => ConfigurationPanel.Root.GetByRole(AriaRole.Alert).Filter(new() { HasTextRegex = ParseErrorText() });

    // A plain paragraph under the editor, not an alert.
    public ILocator UnsafeNumberWarning => ConfigurationPanel.Root.GetByText("A number is larger than 2^53 and loses precision; store it as a string.", new() { Exact = true });

    // The 409 panel of a save its schema rejects: its title, a list item "<pointer> <message>" per error and "Save
    // anyway". A Nuxt UI alert has no alert role, so it is the innermost component root (data-slot) with the title.
    public ILocator SchemaErrors => ConfigurationPanel.Root.Locator("[data-slot=\"root\"]")
        .Filter(new() { Has = app.Page.GetByText("The configuration does not match its schema", new() { Exact = true }) })
        .Last;

    // The panel's "Save anyway"; the confirmation has a button of the same name (SaveAnywayDialog).
    public ILocator SaveAnywayButton => SchemaErrors.GetByRole(AriaRole.Button, new() { Name = "Save anyway", Exact = true });

    // The confirmation of Save anyway (useConfirm): "Cancel" and "Save anyway".
    public ConfirmDialog SaveAnywayDialog => new(app, "Save although the schema fails?");

    public ConfigurationConflictDialog ConflictDialog => new(app);

    // The form of Form mode (a schema applies).
    public SchemaForm Form => new(ConfigurationPanel.Root);

    // The Versions tab's history.
    public VersionsView Versions => new(app, ConfigurationPanel.Root);

    // The Schema tab's applied schemas and the combined schema.
    public CombinedSchemaView CombinedSchema => new(app, ConfigurationPanel.Root);

    // The badge of the shown version in the Edit tab's toolbar, "v<n>".
    public ILocator VersionBadge(int version)
    {
        return ConfigurationPanel.Root.GetByText(string.Create(CultureInfo.InvariantCulture, $"v{version}"), new() { Exact = true });
    }

    // The footer's counter with exactly the number of changes.
    public ILocator Changes(int count)
    {
        var text = count == 1 ? "1 change" : string.Create(CultureInfo.InvariantCulture, $"{count} changes");
        return ConfigurationPanel.Root.GetByText(text, new() { Exact = true });
    }

    // "Delete own" asks this typed confirmation: the key into "Type <key> to confirm", then "Delete".
    public ConfirmDialog DeleteOwnDialog(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new ConfirmDialog(app, $"Delete the own configuration of {key}?");
    }

    // Opens the Edit tab with the list's filter (?q=), as the list opens a row, so the tabs and Close keep it; waits
    // like OpenAsync.
    public async Task OpenFilteredAsync(string key, string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);

        await app.GotoAsync(AppRoutes.WithQuery(routes.Configuration(key), ("q", filter)));
        await Expect(NoConfiguration.Or(JsonMode)).ToBeVisibleAsync();
    }

    // Creates the own configuration from the type's template (version 1); the editor replaces the empty state.
    public async Task StartFromTemplateAsync()
    {
        await Expect(NoConfiguration).ToBeVisibleAsync();
        await StartFromTemplateButton.ClickAsync();
        await Expect(JsonMode).ToBeVisibleAsync();
    }

    // Saves with Ctrl/Cmd+S pressed in a control of the page (Form mode has no editor to press it in: the window's
    // shortcut saves) and waits for the toast of exactly the expected new version.
    public async Task SaveWithShortcutAsync(ILocator control, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 1);

        await Expect(SaveButton).ToBeEnabledAsync();
        await control.PressAsync("ControlOrMeta+S");
        await Expect(app.Toast(string.Create(CultureInfo.InvariantCulture, $"Configuration saved (v{expectedVersion})"))).ToBeVisibleAsync();
    }

    // Opens another tab of the detail by its link (Effective, Resolved, Versions, Schema) and waits for its URL.
    public async Task OpenTabAsync(string tab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tab);

        await ConfigurationPanel.Root.GetByRole(AriaRole.Link, new() { Name = tab, Exact = true }).ClickAsync();
        await Expect(app.Page).ToHaveURLAsync(new Regex($"/{Regex.Escape(tab.ToLowerInvariant())}(?:[?#].*)?$"));
    }

    [GeneratedRegex(@"^\s*\d+ changes?\s*$")]
    private static partial Regex ChangeCounterText();

    [GeneratedRegex(@"^\s*Line \d+: ")]
    private static partial Regex ParseErrorText();
}
