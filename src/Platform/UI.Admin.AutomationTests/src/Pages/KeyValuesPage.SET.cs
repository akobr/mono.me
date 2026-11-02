using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's members of the values store: the filters a link of Settings opens it with (?level=<API
// level>&state=…). The page reads ?state=Deleted, and also ?state=All, as the Deleted keys (key-values.vue); any other
// state, or none, as the Active ones.
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
