using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's members of the values store: the filters a link of Settings opens it with (?level=<API
// level>&state=All, the keys of the level in every state). Admin UI bug (424-152 review): key-values.vue reads
// ?state=All as the Deleted keys, like ?state=Deleted, so such a link shows "No deleted keys" until Active is picked;
// once fixed, it lists the keys at once.
public sealed partial class KeyValuesPage
{
    // The select of the level filter ("All levels", "Public", "Standard", …).
    public ILocator ListLevelFilter => app.Page.GetByRole(AriaRole.Combobox, new() { Name = "Level filter", Exact = true });

    // The empty state of the Deleted keys.
    public ILocator NoDeletedKeys => app.Page.GetByText("No deleted keys", new() { Exact = true });

    // The toggle of the listed state: "Active" or "Deleted".
    public ILocator ListStateButton(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = state, Exact = true });
    }
}
