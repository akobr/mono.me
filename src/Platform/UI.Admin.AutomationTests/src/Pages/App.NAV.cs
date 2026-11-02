using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The members of the shell that the Navigation section uses: the docked sidebar in every state (the icon rail has no
// link names, so the Sidebar locator does not find it), its pin and collapse buttons, the slideover of an unpinned
// sidebar, and the keyboard shortcuts, which the app ignores while a text box, a text area or an editor has the focus.
public sealed partial class App
{
    // The brand's button of a pinned sidebar (hidden in the icon rail); the palette's action is "Unpin the sidebar".
    public ILocator UnpinSidebarButton => Page.GetByRole(AriaRole.Button, new() { Name = "Unpin sidebar", Exact = true });

    // The brand's button in the slideover of an unpinned sidebar; the palette's action is "Pin the sidebar".
    public ILocator PinSidebarButton => Page.GetByRole(AriaRole.Button, new() { Name = "Pin sidebar", Exact = true });

    // In the footer of the expanded docked sidebar; "Expand sidebar" (ExpandSidebarButton) in the icon rail.
    public ILocator CollapseSidebarButton => Page.GetByRole(AriaRole.Button, new() { Name = "Collapse sidebar", Exact = true });

    // The slideover of an unpinned sidebar, at every width (a pinned sidebar's slideover below 1024 px is "Open
    // sidebar").
    public ILocator UnpinnedNavigationDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Navigation", Exact = true });

    // The menu of the docked sidebar, expanded or the icon rail: the nav with the link to /orgs, found by its address
    // because the rail's links have no name. The slideover's menu matches too, while it is open.
    public ILocator DockedNavigationMenu => Page.Locator("nav").Filter(new() { Has = Page.Locator("a[href$=\"/orgs\"]") });

    // Takes the focus from the focused element (a list's filter may hold it), so the app's keyboard shortcuts apply.
    public async Task BlurAsync()
    {
        await Page.EvaluateAsync("() => { if (document.activeElement instanceof HTMLElement) document.activeElement.blur(); }");
    }

    // Presses a shortcut of the app, a single key ("?", "[") or a chain ("g", "c"), with no text box focused: blurs the
    // focused element first, because a page may focus its search after a navigation, then presses the keys one after
    // the other, well within the app's 800 ms for a chain.
    public async Task PressShortcutAsync(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfZero(keys.Length);

        await BlurAsync();
        foreach (var key in keys)
        {
            await Page.Keyboard.PressAsync(key);
        }
    }

    // Picks the first option of the open command palette (OpenPaletteAsync, then the search filled) once its whole name
    // is the expected one: the palette filters about 100 ms after the input, and until then the unfiltered options move
    // under the pointer. Returns once the palette closed.
    public async Task PickFirstPaletteOptionAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await PickFirstPaletteOptionAsync(new Regex($"^{Regex.Escape(name)}$"));
    }

    public async Task PickFirstPaletteOptionAsync(Regex name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var first = CommandPalette.GetByRole(AriaRole.Option).First;
        await Expect(first).ToHaveAccessibleNameAsync(name);
        await first.ClickAsync();
        await Expect(CommandPalette).ToBeHiddenAsync();
    }

    // A link of a navigation menu (MenuAsync) by its exact address below the base, e.g. "acme/billing/members": two
    // links are named "Members", and the icon rail's links have no name at all.
    public ILocator NavigationMenuLink(ILocator menu, string path)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(path);

        return menu.Locator($"a[href=\"/{path.Trim('/')}\"]");
    }

    // The label of the view section in a navigation menu, "View · <view>" (U+00B7); the default view's has the badge
    // "prod" after the name.
    public ILocator NavigationViewLabel(ILocator menu, string view)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(view);

        return menu.Locator("[data-slot=\"label\"]").Filter(new() { HasTextRegex = new Regex($@"^\s*View · {Regex.Escape(view)}(?:\s*prod)?\s*$") });
    }
}
