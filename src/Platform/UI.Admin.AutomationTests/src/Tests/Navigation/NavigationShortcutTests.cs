using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

// Keyboard Shortcuts: the help ("?", the user menu and the palette) and the go-to chains (g a, g c, g g, g o, g s, g t,
// g e), which open the sections of the last view. The app ignores its shortcuts while a text box has the focus, so a
// test blurs it first (App.PressShortcutAsync).
[Area(Area.Navigation)]
public sealed class NavigationShortcutTests : PlatformTest
{
    // NAV-KEY-01: The keyboard shortcuts help opens from ?, the user menu and the palette.
    // "?" opens the help with its three groups and its shortcuts, Escape closes it; the user menu's "Keyboard
    // shortcuts" and the palette's action open it too.
    [Test]
    [TestCase(115)]
    public async Task Shortcuts_Help_OpensFromKeyUserMenuAndPalette()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        var help = new ShortcutsHelpDialog(App);
        await help.OpenWithKeyAsync();
        foreach (var group in new[] { "General", "Go to", "Lists and details" })
        {
            await Expect(help.Group(group)).ToBeVisibleAsync();
        }

        foreach (var shortcut in new[] { "Command palette", "Toggle the sidebar", "Show this help", "Export & Import", "Close the detail" })
        {
            await Expect(help.Shortcut(shortcut)).ToBeVisibleAsync();
        }

        await help.CloseAsync();

        var userMenu = await App.OpenUserMenuAsync();
        foreach (var item in new[] { "Account", "Organizations", "Sign out" })
        {
            await Expect(userMenu.GetByRole(AriaRole.Menuitem, new() { Name = item, Exact = true })).ToBeVisibleAsync();
        }

        // Its name ends in its key, "?".
        var shortcutsItem = App.UserMenuItem("Keyboard shortcuts");
        await Expect(shortcutsItem).ToBeVisibleAsync();
        await shortcutsItem.ClickAsync();
        await Expect(help.Root).ToBeVisibleAsync();
        await help.CloseAsync();

        // On /orgs, without a catalog, no annotation ranks before the action.
        await App.NavigateAsync("Organizations");
        await App.ExpectAtAsync(Routes.Organizations);
        await App.RunCommandAsync("Keyboard shortcuts");
        await Expect(help.Root).ToBeVisibleAsync();
    }

    // NAV-KEY-02: Go-to shortcuts open the sections of the last view and are ignored in text boxes.
    // The chains open the view's configurations, graph and annotations; typed into the filter they are text and do not
    // navigate; from the organizations, which have no view, the chains open Modeling's tabs of the last view.
    [Test]
    [TestCase(116)]
    public async Task Shortcuts_GoTo_OpenSectionsOfLastView()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        await App.PressShortcutAsync("g", "c");
        await App.ExpectAtAsync(Routes.Configurations);
        await App.PressShortcutAsync("g", "g");
        await App.ExpectAtAsync(Routes.Graph);
        await App.PressShortcutAsync("g", "a");
        await App.ExpectAtAsync(Routes.Annotations);

        // Once the filter applied (?q=gc, debounced), a navigation of the chain would have happened already.
        await annotations.Filter.ClickAsync();
        await Page.Keyboard.TypeAsync("gc");
        await Expect(annotations.Filter).ToHaveValueAsync("gc");
        await App.ExpectQueryAsync("q", "gc");
        await App.ExpectAtAsync(Routes.Annotations);

        await annotations.FilterAsync(string.Empty);
        await App.PressShortcutAsync("g", "o");
        await App.ExpectAtAsync(Routes.Organizations);

        // A Reader or a Contributor of a live target sees the locks of Modeling or Export & Import; the URLs hold.
        (string Key, string Tab)[] tabs = [("s", "schemas"), ("t", "templates"), ("e", "export-import")];
        foreach (var (key, tab) in tabs)
        {
            await App.PressShortcutAsync("g", key);
            await App.ExpectAtAsync(Routes.ModelingTab(tab));
        }
    }
}
