using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The members of the shell that the Navigation section uses: the docked sidebar in every state (the icon rail has no
// link names, so the Sidebar locator does not find it), its pin and collapse buttons, the slideover of an unpinned
// sidebar, the landing on a section that the address's #hash names, and the keyboard shortcuts, which the app ignores
// while a text box, a text area or an editor has the focus.
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

    // The brand's link to "/" (SidebarBrand.vue), which opens the last context: in the header of the docked sidebar and
    // of the icon rail, outside the menu's nav, and in the slideover, so a phone opens the slideover first (MenuAsync).
    public ILocator HomeLink => Page.GetByRole(AriaRole.Link, new() { Name = "2S Admin home", Exact = true });

    // The page of an address that matches no route ([...path].vue), with "Nothing lives at <path>." and "Go home"; the
    // navbar's title "Not found" shows only from 1536 px.
    public ILocator NotFoundHeading => Page.GetByRole(AriaRole.Heading, new() { Name = "Page not found", Exact = true });

    // "Go home" of "Page not found", a link to "/".
    public ILocator GoHomeLink => Page.GetByRole(AriaRole.Link, new() { Name = "Go home", Exact = true });

    // "Your organizations" of "You have no access here" (NoAccessHeading), a link to /orgs.
    public ILocator YourOrganizationsLink => Page.GetByRole(AriaRole.Link, new() { Name = "Your organizations", Exact = true });

    // Every detail panel on the page, whatever its section (DetailPanel.vue's data-detail-mode): none once a detail
    // closed.
    public ILocator DetailPanels => Page.Locator("[data-detail-mode]");

    // The menu of the docked sidebar, expanded or the icon rail: the nav with the link to /orgs, found by its address
    // because the rail's links have no name. The slideover's menu matches too, while it is open.
    public ILocator DockedNavigationMenu => Page.Locator("nav").Filter(new() { Has = Page.Locator("a[href$=\"/orgs\"]") });

    // The landing on a section that the #hash of the address names (composables/useHashScroll.ts): settings#secrets,
    // machines#shared-certificates and members#invitations, which the old project pages /secrets, /certificates and
    // /invitations redirect to. The address ends in #<id>, and the section (section#<id>, tabindex -1) is in view and has
    // the focus, as after a followed anchor; the page scrolls only once the content above the section loaded, which the
    // assertions wait for.
    public async Task ExpectHashLandedAsync(ILocator section, string id)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await Expect(Page).ToHaveURLAsync(new Regex($"#{Regex.Escape(id)}$"));
        await Expect(section).ToBeInViewportAsync();
        await Expect(section).ToBeFocusedAsync();
    }

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
