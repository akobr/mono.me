using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's members of ModelingSchemaPage:
// - the Schemas list (…/modeling/schemas, pages/…/modeling/schemas.vue): "New schema" in the navbar, and three
//   sections, each an h2 with a list or "None.": "Type schemas" (always 7 rows, a link named by the type's label to
//   …/schemas/type/<code>, then "v<n>" or "No schema"), "Annotation schemas" (a link named by the key) and
//   "Descendant-type schemas" (a link "<Type> below" and a second link with the key to the annotation). Type labels can
//   be renamed per project, so the rows are found by their links' hrefs;
// - the editor's texts: the description and "Applies to <n> configuration(s) of this view." under the header, the
//   issues of the meta-schema check (a role=alert list under the editor), the alert "Line <n>: …" of a syntax error,
//   the starter document of a schema that does not exist, the alert of a breaking change with its table and "Save
//   anyway", and the typed delete "Delete the <title>?";
// - the form preview ("Form preview", "Reset sample", the details "Sample data", or "No preview"), beside the editor
//   from xl (1280 px) and behind the tabs "Schema" and "Preview" below.
public sealed partial class ModelingSchemaPage
{
    // From this width (xl) the editor and the form preview are side by side; below it they are tabs.
    public const int SideBySidePreviewWidth = 1280;

    // The document of a schema that does not exist yet, as the editor shows it (JSON.stringify with 2 spaces).
    public const string StarterDocument = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": {}
        }
        """;

    // The navbar's button of the list.
    public ILocator NewSchemaButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New schema", Exact = true });

    // "Applies to <n> configuration(s) of this view." under the detail's header (once the catalog loaded).
    public ILocator AppliesTo => Panel.Root.GetByText(AppliesToText());

    // The issues of the meta-schema check under the editor: "<pointer> <message>" per item.
    public ILocator DocumentIssues => Panel.Root.GetByRole(AriaRole.Alert).Filter(new() { Has = app.Page.GetByRole(AriaRole.Listitem) });

    // The alert of a syntax error under the editor, "Line <n>: <parser message>".
    public ILocator SyntaxError => Panel.Root.GetByRole(AriaRole.Alert).Filter(new() { HasTextRegex = SyntaxErrorText() });

    // The table of the configurations a change would break, in the alert NonCompliant; its first column links each
    // configuration.
    public ILocator NonCompliantTable => Panel.Root.GetByRole(AriaRole.Table);

    // "Save anyway" of the alert (the confirmation has a button of the same name: scope that one to its dialog).
    public ILocator SaveAnywayButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Save anyway", Exact = true });

    // The Versions tab of a schema that does not exist.
    public ILocator NoHistory => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "No schema, no history", Exact = true });

    // The panes of the editor below xl: "Schema" (the editor) and "Preview" (the form preview).
    public ILocator SchemaPaneTab => Panel.Root.GetByRole(AriaRole.Tab, new() { Name = "Schema", Exact = true });

    public ILocator PreviewPaneTab => Panel.Root.GetByRole(AriaRole.Tab, new() { Name = "Preview", Exact = true });

    public ILocator FormPreviewHeading => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "Form preview", Exact = true });

    public ILocator ResetSampleButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Reset sample", Exact = true });

    // The summary of the details "Sample data"; the sample's JSON is the pre inside them (SampleData).
    public ILocator SampleDataSummary => Panel.Root.GetByText("Sample data", new() { Exact = true });

    public ILocator SampleData => Panel.Root.Locator("details").Filter(new() { Has = app.Page.GetByText("Sample data", new() { Exact = true }) }).Locator("pre");

    public ILocator NoPreview => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "No preview", Exact = true });

    public ILocator NoPreviewDescription => Panel.Root.GetByText("The preview appears when the schema is a valid object schema.", new() { Exact = true });

    private bool HasSideBySidePreview => app.Page.ViewportSize is not { Width: < SideBySidePreviewWidth };

    // A section of the list by its exact title ("Type schemas", "Annotation schemas", "Descendant-type schemas"): the
    // element around its h2, which holds the rows or "None.".
    public ILocator SchemaListSection(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return app.Page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true, Level = 2 }).Locator("xpath=..");
    }

    // The rows of a section of the list.
    public ILocator SchemaListRows(string title)
    {
        return SchemaListSection(title).GetByRole(AriaRole.Listitem);
    }

    // "None." of an empty section of the list.
    public ILocator NoSchemasIn(string title)
    {
        return SchemaListSection(title).GetByText("None.", new() { Exact = true });
    }

    // The row of a type's schema in "Type schemas", by its code: "v<n>" or "No schema".
    public ILocator TypeSchemaRow(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return SchemaRowLinking("Type schemas", $"/modeling/schemas/type/{type}");
    }

    // The link of a type's schema in its row, named by the type's label.
    public ILocator TypeSchemaLink(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return app.Page.Locator($"a[href$=\"/modeling/schemas/type/{type}\"]");
    }

    // The row of an annotation's schema in "Annotation schemas".
    public ILocator AnnotationSchemaRow(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return SchemaRowLinking("Annotation schemas", $"/modeling/schemas/annotation/{Uri.EscapeDataString(key)}");
    }

    // The link of an annotation's schema, named by the key.
    public ILocator AnnotationSchemaLink(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.Locator($"a[href$=\"/modeling/schemas/annotation/{Uri.EscapeDataString(key)}\"]");
    }

    // The row of a descendant-type schema in "Descendant-type schemas": the link "<Type> below", the key's link and
    // "v<n>".
    public ILocator DescendantSchemaRow(string key, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return SchemaRowLinking("Descendant-type schemas", $"/modeling/schemas/annotation/{Uri.EscapeDataString(key)}/type/{type}");
    }

    // A field of the form preview by its exact label: the property's title, or its humanized name.
    public ILocator PreviewField(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Panel.Root.GetByLabel(label, new() { Exact = true });
    }

    // The typed delete of the open schema, "Delete the <title>?" ("Delete the Schema of <key>?", "Delete the
    // Responsibility schema?"), with the field "Type delete to confirm".
    public ConfirmDialog DeleteDialog(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new ConfirmDialog(app, $"Delete the {title}?");
    }

    // Opens the Schemas list by its URL and waits for its sections.
    public async Task OpenSchemaListAsync()
    {
        await app.GotoAsync(routes.ModelingTab("schemas"));
        await app.WaitForReadyAsync();
        await Expect(SchemaListSection("Type schemas")).ToBeVisibleAsync();
    }

    // Saves the document the editor holds (the starter document, or one set before) of a schema that does not exist
    // yet, and waits for the toast "Schema saved (v1)".
    public async Task CreateWithCurrentDocumentAsync()
    {
        await SubmitAsync(CreateButton, 1);
    }

    // Replaces the first occurrence of a text in the document (e.g. "string" by "number"), as one insertion of the
    // whole changed document.
    public async Task ReplaceInDocumentAsync(string oldText, string newText)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        var text = await Document.TextAsync();
        var index = text.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"The schema document has no '{oldText}': {text}");
        }

        await Document.ReplaceAsync(string.Concat(text.AsSpan(0, index), newText, text.AsSpan(index + oldText.Length)));
    }

    // Shows the form preview: the tab "Preview" below xl; beside the editor from xl, where nothing is needed.
    public async Task ShowPreviewPaneAsync()
    {
        if (!HasSideBySidePreview)
        {
            await PreviewPaneTab.ClickAsync();
            await Expect(PreviewPaneTab).ToHaveAttributeAsync("aria-selected", "true");
        }

        await Expect(FormPreviewHeading).ToBeVisibleAsync();
    }

    // Shows the editor again: the tab "Schema" below xl; nothing from xl.
    public async Task ShowSchemaPaneAsync()
    {
        if (!HasSideBySidePreview)
        {
            await SchemaPaneTab.ClickAsync();
            await Expect(SchemaPaneTab).ToHaveAttributeAsync("aria-selected", "true");
        }

        await Expect(Document.Content).ToBeVisibleAsync();
    }

    // The text of "Applies to <n> configuration(s) of this view." for n.
    public static string AppliesToConfigurations(int count)
    {
        return string.Create(CultureInfo.InvariantCulture, $"Applies to {count} configuration{(count == 1 ? string.Empty : "s")} of this view.");
    }

    // The row of a section whose link's href ends with the path.
    private ILocator SchemaRowLinking(string section, string hrefEnd)
    {
        return SchemaListRows(section).Filter(new() { Has = app.Page.Locator($"a[href$=\"{hrefEnd}\"]") });
    }

    [GeneratedRegex(@"^\s*Applies to \d+ configurations? of this view\.\s*$")]
    private static partial Regex AppliesToText();

    [GeneratedRegex(@"^\s*Line \d+: ")]
    private static partial Regex SyntaxErrorText();
}
