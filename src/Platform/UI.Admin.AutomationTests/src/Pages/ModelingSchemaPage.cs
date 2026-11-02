using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The schema editor of Modeling's Schemas tab (components/schemas/SchemaDetail.vue in the detail section#schema-detail):
// a type's schema (…/modeling/schemas/type/<type>, "Execution schema"), an annotation's (…/schemas/annotation/<key>,
// "Schema of <key>") or one for an annotation's descendants of a type (…/annotation/<key>/type/<type>, "Execution
// schema below <key>"); the region and its heading are named so. The tabs "Edit" and "Versions" (?tab=versions,
// VersionsView). Edit has the JSON or YAML editor "Schema document", validated against the JSON Schema meta-schema
// (issues listed under it; Save is disabled while there are any), and the form preview beside it from xl (1280 px) or
// behind the tabs "Schema" and "Preview" below. A schema that does not exist starts with a starter document and the
// button "Create" (no Delete); an existing one has "Delete", "Revert" and "Save". Saving (Ctrl/Cmd+S too) says "Schema
// saved (v<n>)"; a change that existing configurations would break shows the alert "Existing configurations would not
// comply" with "Save anyway" instead. Contributor and up; the never-saved editor counts as changed, so leaving it asks
// "Discard changes?".
public sealed partial class ModelingSchemaPage(App app, AppRoutes routes)
{
    public DetailPanel Panel { get; } = new(app, "schema-detail");

    public CodeEditor Document => new(app, "Schema document", Panel.Root);

    // The format switch of the editor (radiogroup "Editing mode").
    public ILocator JsonMode => Panel.Root.GetByRole(AriaRole.Radio, new() { Name = "JSON", Exact = true });

    public ILocator YamlMode => Panel.Root.GetByRole(AriaRole.Radio, new() { Name = "YAML", Exact = true });

    public ILocator EditTab => Panel.Root.GetByRole(AriaRole.Tab, new() { Name = "Edit", Exact = true });

    public ILocator VersionsTab => Panel.Root.GetByRole(AriaRole.Tab, new() { Name = "Versions", Exact = true });

    // The primary button of a schema that does not exist yet; Save of an existing one.
    public ILocator CreateButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true });

    public ILocator SaveButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

    public ILocator RevertButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Revert", Exact = true });

    // Only for an existing schema; it asks "Delete the <title>?" with the typed confirmation "delete".
    public ILocator DeleteButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });

    // The alert of a change existing configurations would break, with their table and "Save anyway".
    public ILocator NonCompliant => Panel.Root.GetByText("Existing configurations would not comply", new() { Exact = true });

    public VersionsView Versions => new(app, Panel.Root);

    // Opens the schema of an annotation (it applies to the annotation and its descendants) and waits for its editor.
    public async Task OpenAnnotationAsync(string key)
    {
        await OpenAsync(routes.AnnotationSchema(key));
    }

    // Replaces the schema document as JSON.
    public async Task SetDocumentAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        await JsonMode.ClickAsync();
        await Document.ReplaceAsync(json);
    }

    // Creates the schema that does not exist yet with its document and waits for the toast "Schema saved (v1)".
    public async Task CreateAsync(string json)
    {
        await SetDocumentAsync(json);
        await SubmitAsync(CreateButton, 1);
    }

    // Saves the changed document of an existing schema and waits for the toast of exactly the expected new version.
    public async Task SaveAsync(int expectedVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 2);
        await SubmitAsync(SaveButton, expectedVersion);
    }

    // Opens a schema detail and waits for its editor (the Edit tab), which shows once the schema (or its absence)
    // loaded; on a phone the detail is full screen and hides the navbar, so this does not wait for the user menu.
    private async Task OpenAsync(string path)
    {
        await app.GotoAsync(path);
        await Expect(Document.Content).ToBeVisibleAsync();
    }

    // With the editor's shortcut once the button is enabled (the document is valid), not a click: the toast of an
    // earlier save covers the save bar's button at the bottom right for its 5 seconds, and a pointer on a toast keeps
    // it open. Create and Save run the same save.
    private async Task SubmitAsync(ILocator button, int expectedVersion)
    {
        await Expect(button).ToBeEnabledAsync();
        await Document.PressSaveAsync();
        await Expect(app.Toast(string.Create(CultureInfo.InvariantCulture, $"Schema saved (v{expectedVersion})"))).ToBeVisibleAsync();
    }
}
