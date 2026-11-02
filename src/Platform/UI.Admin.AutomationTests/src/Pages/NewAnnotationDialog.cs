using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "New annotation" of the annotations and the graph (components/annotations/AnnotationCreateDialog.vue), Contributor and
// up: the navbar's "New" (from sm, 640 px) or its icon button "New annotation" (on a phone), the empty state's "New
// annotation" in an empty view, and the palette's "New annotation" open it. The required "Type" select starts at
// Execution; the fields of the type's key parts follow, in key order: the parent parts are comboboxes named by their
// type ("Subject", "Responsibility", "Context", "Unit") that suggest the catalog's names and take a new one through the
// option 'Create "<name>"', the own part is the textbox "Name" (Responsibility, Unit, Subject and Context only). Then
// "Title", "Time zone" (help "Default from the project settings.", the project's default) and the key preview: "Fill
// in the names", the key, "This annotation already exists." or "The API also creates the missing ancestors: …".
// "Create" is enabled for a complete key that does not exist; the toast "<key> created" follows, the dialog closes and
// the detail of the new key opens (…/annotations/<key> or …/graph/<key>, with the page's query). A deleted key or
// ancestor asks "Deleted annotations" first. The dialog keeps its type and its parent parts when it closes, and after a
// create too (only Name and Title are cleared). Field names follow the type names of the project's settings and the
// Preferences; the defaults of a fresh browser are the built-in names this page object uses.
public sealed partial class NewAnnotationDialog(App app)
{
    // The built-in type of each code, and its key parts by the names of their fields, in key order.
    private static readonly Dictionary<string, (string Type, string[] Parts)> Types = new(StringComparer.Ordinal)
    {
        ["rst"] = ("Responsibility", ["Name"]),
        ["unt"] = ("Unit", ["Responsibility", "Name"]),
        ["sbt"] = ("Subject", ["Name"]),
        ["usg"] = ("Usage", ["Subject", "Responsibility"]),
        ["cnt"] = ("Context", ["Subject", "Name"]),
        ["exe"] = ("Execution", ["Subject", "Responsibility", "Context"]),
        ["uxe"] = ("Unit of execution", ["Subject", "Responsibility", "Context", "Unit"]),
    };

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New annotation", Exact = true });

    // The navbar's button from sm (640 px).
    public ILocator NewButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New", Exact = true });

    // The navbar's icon button on a phone, and the empty state's button of an empty view.
    public ILocator NewAnnotationButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New annotation", Exact = true });

    // A required field's accessible name ends in '*' ("Type*").
    public ILocator TypeSelect => Root.GetByRole(AriaRole.Combobox, new() { NameRegex = TypeName() });

    public ILocator NameField => Part("Name");

    public ILocator TitleField => Root.GetByRole(AriaRole.Textbox, new() { Name = "Title", Exact = true });

    public TimeZoneSelect TimeZone => new(app, Root.GetByRole(AriaRole.Button, new() { Name = "Time zone", Exact = true }));

    // The key of the preview; the text of its code element is the key.
    public ILocator KeyPreview => Root.GetByTitle(KeyTitle());

    public ILocator FillInTheNames => Root.GetByText("Fill in the names", new() { Exact = true });

    public ILocator AlreadyExists => Root.GetByText("This annotation already exists.", new() { Exact = true });

    // "The API also creates the missing ancestors: usg.s.r, cnt.s.c, rst.r, sbt.s". A substring: the element's text
    // starts with a space of the template, which an anchored regex of GetByText does not skip.
    public ILocator MissingAncestors => Root.GetByText("The API also creates the missing ancestors:");

    public ILocator CreateButton => Root.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // An option of the open Type select, by the type's exact name ("Unit" is not "Unit of execution").
    public ILocator TypeOption(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = type, Exact = true });
    }

    // The field of a key part by its name: the combobox of a parent part ("Subject", "Responsibility", "Context",
    // "Unit") or the textbox "Name".
    public ILocator Part(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var name = new Regex($@"^{Regex.Escape(field)}\*?$");
        return string.Equals(field, "Name", StringComparison.Ordinal)
            ? Root.GetByRole(AriaRole.Textbox, new() { NameRegex = name })
            : Root.GetByRole(AriaRole.Combobox, new() { NameRegex = name });
    }

    // Opens the dialog from the navbar of the annotations or the graph ("New" from 640 px, "New annotation" below; in
    // an empty view the empty state's button opens the same dialog). Wait for the catalog first where the test needs
    // the suggestions or the exists check (AnnotationsPage.OpenNewAnnotationAsync does).
    public async Task OpenAsync()
    {
        await (app.IsPhone ? NewAnnotationButton.First : NewButton).ClickAsync();
        await Expect(Root).ToBeVisibleAsync();
    }

    // Picks the type by its name ("Execution", "Unit of execution", …); the part fields of the type replace the others.
    public async Task SelectTypeAsync(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        await TypeSelect.ClickAsync();
        await TypeOption(type).ClickAsync();
        await Expect(TypeSelect).ToContainTextAsync(type);
    }

    // Fills a key part: the textbox "Name", or a parent part's combobox, whose suggestion of exactly that name or its
    // option 'Create "<name>"' (shown when no suggestion contains the text) is picked. The list stays open after
    // 'Create "<name>"', so a click on the dialog's title closes it: a click outside the list, where Escape could close
    // the dialog instead once the list is closed.
    public async Task FillPartAsync(string field, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var input = Part(field);
        if (string.Equals(field, "Name", StringComparison.Ordinal))
        {
            await input.FillAsync(name);
            return;
        }

        // Cleared first: the dialog keeps its fields when it closes, and the same text again opens no list.
        await input.FillAsync(string.Empty);
        await input.FillAsync(name);

        var option = app.Page.GetByRole(AriaRole.Option, new() { Name = $"Create \"{name}\"", Exact = true })
            .Or(app.Page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }));
        await option.First.ClickAsync();
        await Expect(input).ToHaveValueAsync(name);
        await Root.GetByRole(AriaRole.Heading, new() { Name = "New annotation", Exact = true }).ClickAsync();
        await Expect(input).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // Fills the type and the parts of a key (e.g. exe.<s>.<r>.<c>), with the built-in type names, and waits for its
    // preview.
    public async Task FillAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var segments = key.Split('.');
        if (!Types.TryGetValue(segments[0], out var type) || segments.Length != type.Parts.Length + 1)
        {
            throw new ArgumentException($"'{key}' is not an annotation key of one of the types {string.Join(", ", Types.Keys)} with its number of parts.", nameof(key));
        }

        await SelectTypeAsync(type.Type);
        for (var index = 0; index < type.Parts.Length; index++)
        {
            await FillPartAsync(type.Parts[index], segments[index + 1]);
        }

        await Expect(KeyPreview).ToHaveAttributeAsync("title", key);
    }

    // Creates a new annotation of the key (with its missing ancestors) and returns once the toast "<key> created"
    // showed and the dialog closed; the detail of the key opens next. The key must not exist, nor be deleted.
    public async Task CreateAsync(string key, string? title = null)
    {
        await FillAsync(key);
        if (title is not null)
        {
            await TitleField.FillAsync(title);
        }

        await Expect(CreateButton).ToBeEnabledAsync();
        await CreateButton.ClickAsync();
        await Expect(app.Toast($"{key} created")).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    [GeneratedRegex(@"^Type\*?$")]
    private static partial Regex TypeName();

    // The preview's code element is titled with the whole key: <code>.<part>….
    [GeneratedRegex(@"^[a-z]{3}\.")]
    private static partial Regex KeyTitle();
}
