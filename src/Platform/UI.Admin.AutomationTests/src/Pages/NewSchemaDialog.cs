using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "New schema" of the Schemas list (components/schemas/SchemaCreateDialog.vue): the kinds "Type schema" (the default),
// "Annotation schema" and "Descendant-type schema" (radios), the select "Annotation type" (Execution by default; for a
// descendant-type schema only the types that can descend from the key, and the first of them when the selected one
// cannot; disabled with the help "No annotation type descends from <key>." when none can) and the input menu
// "Annotation key" (every key of the view's catalog, virtualized; a typed text alone sets nothing, the key must be
// picked as its option). "Open editor" is enabled once the target is complete and opens the editor with a starter
// document; nothing is saved before. The select's and the input menu's options open in a layer of their own.
public sealed partial class NewSchemaDialog(App app)
{
    public const string KeyPlaceholder = "exe.subject.responsibility.context";

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New schema", Exact = true });

    public ILocator TypeSchemaKind => Kind("Type schema");

    public ILocator AnnotationSchemaKind => Kind("Annotation schema");

    public ILocator DescendantSchemaKind => Kind("Descendant-type schema");

    // The selected type's label is its text.
    public ILocator TypeSelect => Root.GetByRole(AriaRole.Combobox, new() { Name = "Annotation type", Exact = true });

    public ILocator KeyInput => Root.GetByRole(AriaRole.Combobox, new() { Name = "Annotation key", Exact = true });

    public ILocator OpenEditorButton => Root.GetByRole(AriaRole.Button, new() { Name = "Open editor", Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // The options of the open select or input menu.
    public ILocator Options => app.Page.GetByRole(AriaRole.Option);

    public ILocator Kind(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.GetByRole(AriaRole.Radio, new() { Name = label, Exact = true });
    }

    // An option of the open select or input menu by its exact name.
    public ILocator Option(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });
    }

    // The help of a descendant-type schema whose key no type descends from.
    public ILocator NothingDescends(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Root.GetByText($"No annotation type descends from {key}.", new() { Exact = true });
    }

    // "New schema" on the Schemas list, then the dialog.
    public async Task OpenAsync()
    {
        await app.Page.GetByRole(AriaRole.Button, new() { Name = "New schema", Exact = true }).ClickAsync();
        await Expect(Root).ToBeVisibleAsync();
    }

    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    public async Task ChooseKindAsync(string label)
    {
        await Kind(label).ClickAsync();
        await Expect(Kind(label)).ToBeCheckedAsync();
    }

    // Types the whole key into "Annotation key" (the list is virtualized, so the filter brings its option into view),
    // picks its option and waits until the input holds it.
    public async Task PickKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await KeyInput.FillAsync(key);
        await Option(key).ClickAsync();
        await Expect(Options).ToHaveCountAsync(0);
        await Expect(KeyInput).ToHaveValueAsync(key);
    }

    // Opens "Annotation type" and returns its options; CloseTypeSelectAsync closes it again.
    public async Task<ILocator> OpenTypeSelectAsync()
    {
        await TypeSelect.ClickAsync();
        await Expect(Options.First).ToBeVisibleAsync();
        return Options;
    }

    // Closes the open "Annotation type" with Escape, which the select keeps to itself; the dialog stays.
    public async Task CloseTypeSelectAsync()
    {
        await app.Page.Keyboard.PressAsync("Escape");
        await Expect(Options).ToHaveCountAsync(0);
        await Expect(Root).ToBeVisibleAsync();
    }

    // Picks a type of the open "Annotation type" by its position (the types' order is fixed; their labels can be
    // renamed) and waits until the select closed.
    public async Task PickTypeAtAsync(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        await Options.Nth(index).ClickAsync();
        await Expect(Options).ToHaveCountAsync(0);
    }

    // Opens the editor of the target and waits until the dialog closed.
    public async Task OpenEditorAsync()
    {
        await Expect(OpenEditorButton).ToBeEnabledAsync();
        await OpenEditorButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
