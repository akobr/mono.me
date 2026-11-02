using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The members of the context switcher that the Navigation section uses: a menu opened on its own, to check its items
// before one is picked.
public sealed partial class ContextSwitcher
{
    // Opens the menu of a button of the switcher (OrganizationButton, ProjectButton or ViewButton) and returns it. The
    // button is clicked near its start: a long name is truncated, and on a phone the actions of a page's navbar (the
    // Values store's Export and Import) cover the rest of the button.
    public async Task<ILocator> OpenSwitcherMenuAsync(ILocator button)
    {
        ArgumentNullException.ThrowIfNull(button);

        await Expect(button).ToBeVisibleAsync();
        var box = await button.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The button of the context switcher has no box.");
        await button.ClickAsync(new() { Position = new() { X = 8, Y = box.Height / 2 } });
        await Expect(Menu).ToBeVisibleAsync();
        return Menu;
    }

    // Picks an item of the open menu (OpenSwitcherMenuAsync) and waits for the menu to close; the caller checks where
    // the item led, before any role query on the page (reka-ui hides the page while a menu closes).
    public async Task PickOpenMenuItemAsync(ILocator item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await PickAsync(item);
    }
}
