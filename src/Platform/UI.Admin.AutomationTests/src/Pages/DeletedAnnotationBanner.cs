using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The banner of a deleted annotation's detail (components/annotations/DeletedAnnotationBanner.vue), above its history:
// the alert "<key> is deleted" with "Deleted on <yyyy-mm-dd> by <actor>", then " with <n> descendant(s)" for the root
// of a deletion or " together with <root key>" for a key deleted with an ancestor, and "Purged automatically on
// <yyyy-mm-dd>.". Contributors and up restore it: "Restore" (with its descendants) or "More restore options" ›
// "Restore without descendants" (the deleted children become roots of their own deletion). Either shows the toast
// "<key> restored", and the detail loads the annotation's form again.
public sealed partial class DeletedAnnotationBanner(App app)
{
    public ILocator Root => app.Page.GetByTestId("deleted-banner");

    // The description (Nuxt UI's alert slot), "Deleted on … by …. Purged automatically on ….".
    public ILocator Description => Root.Locator("[data-slot=\"description\"]");

    public ILocator RestoreButton => Root.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true });

    public ILocator MoreRestoreOptionsButton => Root.GetByRole(AriaRole.Button, new() { Name = "More restore options", Exact = true });

    public ILocator RestoreWithoutDescendantsItem => app.Page.GetByRole(AriaRole.Menuitem, new() { Name = "Restore without descendants", Exact = true });

    // The banner's title, "<key> is deleted".
    public ILocator Title(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Root.GetByText($"{key} is deleted", new() { Exact = true });
    }

    // Restores the key with its descendants and returns once the toast "<key> restored" showed and the banner is gone.
    public async Task RestoreAsync(string key)
    {
        await RestoreButton.ClickAsync();
        await ExpectRestoredAsync(key);
    }

    // Restores the key alone (More restore options › Restore without descendants) and returns once the toast "<key>
    // restored" showed and the banner is gone.
    public async Task RestoreWithoutDescendantsAsync(string key)
    {
        await MoreRestoreOptionsButton.ClickAsync();
        await RestoreWithoutDescendantsItem.ClickAsync();
        await ExpectRestoredAsync(key);
    }

    private async Task ExpectRestoredAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await Expect(app.Toast($"{key} restored")).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
