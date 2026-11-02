using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The keyboard shortcuts help (components/shell/ShortcutsHelp.vue), the modal "Keyboard shortcuts" of every page of the
// default layout: "?" (with no text box focused), the user menu's "Keyboard shortcuts" and the palette's action open
// it, Escape closes it. Three groups, each an h3 with a shortcut per line (its description, then its keys):
// "General" (Command palette, Focus the search box, Toggle the sidebar, Show this help), "Go to" (Organizations,
// Annotations, Configurations, Graph, Templates, Schemas, Export & Import) and "Lists and details" (Next row, Previous
// row, Open the detail, Close the detail, Save, Cycle Form, JSON and YAML).
public sealed partial class ShortcutsHelpDialog(App app)
{
    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Keyboard shortcuts", Exact = true });

    // A group's heading: "General", "Go to" or "Lists and details".
    public ILocator Group(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return Root.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true, Level = 3 });
    }

    // A shortcut's description, by its exact text ("Command palette", "Export & Import", …).
    public ILocator Shortcut(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return Root.GetByText(description, new() { Exact = true });
    }

    // Opens the help with "?", with no text box focused (the app ignores its shortcuts in one).
    public async Task OpenWithKeyAsync()
    {
        await app.PressShortcutAsync("?");
        await Expect(Root).ToBeVisibleAsync();
    }

    // Closes the help with Escape.
    public async Task CloseAsync()
    {
        await app.Page.Keyboard.PressAsync("Escape");
        await Expect(Root).ToBeHiddenAsync();
    }
}
