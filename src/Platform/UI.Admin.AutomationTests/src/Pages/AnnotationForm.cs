using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Annotation tab of an annotation's detail (components/annotations/AnnotationForm.vue): one form in three editing
// modes, the radiogroup "Editing mode" with Form, JSON and YAML (?format= when the mode differs from the preference,
// Form by default; Ctrl/Cmd+Shift+F cycles them). Form mode: the identity line (the type's icon and the key; no Name,
// Type or Key field), Title, Description (the tabs Write and Preview, Markdown rendered safely: raw HTML as text, only
// http(s), mailto and relative links, opened in a new tab; "<n> / 10,000" above 9,000 characters), Documentation link
// ("Open the link" once filled), the switch Disabled, Valid from and Expires (a date and a time each, with Clear),
// Time zone, the Unit fields of a unit (Unit type, Unit definition and the Cron hint of Time), Labels, Values and the
// Relations. JSON and YAML: the editor "Annotation document" of the whole annotation, whose key fields must not change
// (an alert under it, and Save disabled), and the schema's messages under it. The save bar (Contributors and up):
// "Unsaved changes", Revert and Save (Ctrl/Cmd+S, the toast "Annotation saved"). A Reader gets the badge "Read-only",
// disabled fields, a read-only editor and no save bar. The messages of the fields: "At most 200 characters." (Title),
// "At most 10,000 characters." (Description), "Use an absolute http(s) URL.", "Expires must be after Valid from.",
// "Enter a unit definition.", "Duplicate keys: <keys>".
public sealed partial class AnnotationForm(App app, string section = AnnotationDetail.AnnotationsSection)
{
    // As the app writes a document (JSON.stringify(value, null, 2)): indented, nothing escaped that JSON allows.
    private static readonly JsonSerializerOptions DocumentOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public DetailPanel Panel { get; } = new(app, $"{section}-detail");

    // The form header's "Editing mode"; Values' "Edit all as JSON/YAML" adds a second one below it.
    public ILocator EditingMode => Panel.Root.GetByRole(AriaRole.Radiogroup, new() { Name = "Editing mode", Exact = true }).First;

    // The JSON or YAML text of the whole annotation (CodeMirror; read-only for a Reader).
    public CodeEditor Document => new(app, "Annotation document", Panel.Root);

    public ILocator ReadOnlyBadge => Panel.Root.GetByText("Read-only", new() { Exact = true });

    public ILocator Title => Panel.Root.GetByRole(AriaRole.Textbox, new() { Name = "Title", Exact = true });

    // The textarea of the Write tab.
    public ILocator Description => Panel.Root.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });

    // The Preview tab's rendered Markdown (MarkdownText has no role of its own, so its class).
    public ILocator DescriptionPreview => Panel.Root.Locator(".markdown");

    // The Preview tab of an empty description.
    public ILocator NothingToPreview => Panel.Root.GetByText("Nothing to preview.", new() { Exact = true });

    public ILocator DocumentationLink => Panel.Root.GetByRole(AriaRole.Textbox, new() { Name = "Documentation link", Exact = true });

    // In the Documentation link field once it holds a link; it opens the link in a new tab.
    public ILocator OpenTheLinkButton => Panel.Root.GetByLabel("Open the link", new() { Exact = true });

    public ILocator DisabledSwitch => Panel.Root.GetByRole(AriaRole.Switch, new() { Name = "Disabled", Exact = true });

    // The date of Expires (year, month and day segments; their placeholders while empty).
    public ILocator ExpiresDate => Panel.Root.GetByRole(AriaRole.Group, new() { Name = "Expires date", Exact = true });

    // "Clear" of the Expires field, shown while it holds a value; Valid from has its own.
    public ILocator ExpiresClearButton => ExpiresDate.Locator("xpath=..").GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true });

    // The Unit fields, only on a unit (unt): "Unit type", "Unit definition" and the Cron hint.
    public ILocator UnitFields => Panel.Root.GetByTestId("unit-fields");

    // Suggests Time, Message and Volume (and the current custom value, e.g. event), or takes free text.
    public ILocator UnitType => Panel.Root.GetByRole(AriaRole.Combobox, new() { Name = "Unit type", Exact = true });

    public ILocator UnitDefinition => Panel.Root.GetByRole(AriaRole.Textbox, new() { Name = "Unit definition", Exact = true });

    // "A Cron expression usually has 5 or 6 fields, for example */15 * * * *." of a Time definition that has not.
    public ILocator UnitCronHint => Panel.Root.GetByTestId("unit-cron-hint");

    // The labels as tags, with the catalog's labels as suggestions ("<label>" and its count) and 'Create "<text>"'.
    public ILocator Labels => Panel.Root.GetByRole(AriaRole.Combobox, new() { Name = "Labels", Exact = true });

    public ILocator NoValues => Panel.Root.GetByText("No values.", new() { Exact = true });

    public ILocator AddValueButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Add value", Exact = true });

    // The switch "Edit all as JSON/YAML" (its own label): the field's label "Values" is its aria-label, so its
    // accessible name is "Values".
    public ILocator ValuesEditAllSwitch => Panel.Root.GetByRole(AriaRole.Switch, new() { Name = "Values", Exact = true });

    // The visible label of ValuesEditAllSwitch.
    public ILocator ValuesEditAllLabel => Panel.Root.GetByText("Edit all as JSON/YAML", new() { Exact = true });

    // The editor of the whole Values map, once "Edit all as JSON/YAML" is on.
    public CodeEditor ValuesDocument => new(app, "Values", Panel.Root);

    // "Duplicate keys: <keys>" under the rows.
    public ILocator DuplicateKeys => Panel.Root.GetByText(DuplicateKeysText());

    public ILocator UnsavedChanges => Panel.Root.GetByText("Unsaved changes", new() { Exact = true });

    public ILocator RevertButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Revert", Exact = true });

    // "Save" with its shortcut (⌘/Ctrl S) in the name.
    public ILocator SaveButton => Panel.Root.GetByRole(AriaRole.Button, new() { NameRegex = SaveName() });

    // The alerts under the text editor: a syntax error, the key fields that changed ("<field> is part of the key and
    // cannot change."), and the form's messages ("<field> <message>").
    public ILocator DocumentAlerts => Panel.Root.GetByRole(AriaRole.Alert);

    // A radio of "Editing mode": "Form", "JSON" or "YAML".
    public ILocator Mode(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return EditingMode.GetByRole(AriaRole.Radio, new() { Name = name, Exact = true });
    }

    // The tab "Write" or "Preview" of Description.
    public ILocator DescriptionTab(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Panel.Root.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }

    // A message of a field, a hint or a help text of the form by its exact text, e.g. "At most 200 characters.".
    public ILocator FieldMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return Panel.Root.GetByText(message, new() { Exact = true });
    }

    // A label's tag in Labels.
    public ILocator LabelTag(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Panel.Root.GetByText(label, new() { Exact = true });
    }

    // The suggestion of an existing label of the catalog: the label and its count, e.g. 1 (count: null for any).
    public ILocator LabelOption(string label, int? count = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var suffix = count is { } known ? $@"\s*{known}" : @"\s*[\d,]*";
        return app.Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($"^{Regex.Escape(label)}{suffix}$") });
    }

    // The option of a label that no suggestion contains yet.
    public ILocator CreateLabelOption(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = $"Create \"{label}\"", Exact = true });
    }

    // The controls of a row of Values (0 first): its key, its type and its value (a textbox for a string, a number
    // field for a number, a switch for a boolean; null, objects and arrays have no "Value" control).
    public ILocator ValueKey(int row)
    {
        return Panel.Root.GetByRole(AriaRole.Textbox, new() { Name = "Value key", Exact = true }).Nth(row);
    }

    public ILocator ValueType(int row)
    {
        return Panel.Root.GetByRole(AriaRole.Combobox, new() { Name = "Value type", Exact = true }).Nth(row);
    }

    public ILocator Value(int row)
    {
        return Panel.Root.GetByLabel("Value", new() { Exact = true }).Nth(row);
    }

    // "Remove <key>" of every row with the key (duplicates have one each).
    public ILocator RemoveValueButton(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Panel.Root.GetByRole(AriaRole.Button, new() { Name = $"Remove {key}", Exact = true });
    }

    // Picks an editing mode and waits until its radio is checked; the text modes show the editor.
    public async Task SelectModeAsync(string name)
    {
        await Mode(name).ClickAsync();
        await Expect(Mode(name)).ToHaveAttributeAsync("aria-checked", "true");
        if (!string.Equals(name, "Form", StringComparison.Ordinal))
        {
            await Expect(Document.Content).ToBeVisibleAsync();
        }
    }

    // The JSON mode's document as an object.
    public async Task<JsonObject> ReadDocumentAsync()
    {
        var text = await Document.TextAsync();
        return JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException($"The annotation document is not a JSON object: {text}");
    }

    // Changes the JSON mode's document: reads it, applies the change and writes it back as one text.
    public async Task EditDocumentAsync(Action<JsonObject> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var document = await ReadDocumentAsync();
        change(document);
        await Document.ReplaceAsync(document.ToJsonString(DocumentOptions));
    }

    // Adds a label in Labels: types it and picks its option, 'Create "<label>"' for a new one or the suggestion of an
    // existing one. The list stays open after a pick, so a click on the identity line closes it.
    public async Task AddLabelAsync(string label, bool existing)
    {
        await Labels.FillAsync(label);
        var option = existing ? LabelOption(label) : CreateLabelOption(label);
        await option.ClickAsync();
        await Expect(LabelTag(label)).ToBeVisibleAsync();
        await Panel.Root.GetByTestId("annotation-identity").ClickAsync();
        await Expect(Labels).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // Picks a type of a Values row (string, number, boolean, null, object, array); it resets the row's value.
    public async Task SetValueTypeAsync(int row, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        await ValueType(row).ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = type, Exact = true }).ClickAsync();
        await Expect(ValueType(row)).ToHaveTextAsync(type);
    }

    // Saves with Ctrl/Cmd+S once Save is enabled (in every mode; a toast can cover the save bar) and returns once the
    // toast "Annotation saved" showed and the form is clean again.
    public async Task SaveAsync()
    {
        await Expect(SaveButton).ToBeEnabledAsync();
        await app.Page.Keyboard.PressAsync("ControlOrMeta+s");
        await Expect(app.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(SaveButton).ToBeDisabledAsync();
    }

    [GeneratedRegex("^Save")]
    private static partial Regex SaveName();

    [GeneratedRegex("^Duplicate keys: ")]
    private static partial Regex DuplicateKeysText();
}
