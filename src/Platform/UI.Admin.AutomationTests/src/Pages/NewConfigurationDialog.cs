using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "New configuration" of the Configurations page (components/configuration/ConfigurationCreateDialog.vue, Contributor
// and up): the description "In <org>/<project>, view <view>." and the field "Annotation", a searchable select of the
// view's annotations without an own configuration. Its trigger is a button named by the field ("Loading the catalog…"
// and disabled until the catalog loaded); the search input ("Search keys…") and the options (named by the key) are
// teleported to the body. A pick moves the focus to "Open editor", so Enter opens the configuration (…/configurations/
// <key>?q=<the list's q>) with its empty state; the dialog creates nothing itself.
public sealed partial class NewConfigurationDialog(App app)
{
    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New configuration", Exact = true });

    public ILocator Description => Root.Locator("[data-slot=\"description\"]");

    public ILocator AnnotationTrigger => Root.GetByRole(AriaRole.Button, new() { Name = "Annotation", Exact = true });

    public ILocator Search => app.Page.GetByPlaceholder("Search keys…", new() { Exact = true });

    public ILocator OpenEditorButton => Root.GetByRole(AriaRole.Button, new() { Name = "Open editor", Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // An option of the open select, by its exact key.
    public ILocator Option(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = key, Exact = true });
    }

    public async Task ExpectOpenAsync()
    {
        await Expect(Root).ToBeVisibleAsync();
    }

    // Opens the select once the catalog loaded (the trigger is enabled) and types the key into its search.
    public async Task SearchAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await Expect(AnnotationTrigger).ToBeEnabledAsync();
        await AnnotationTrigger.ClickAsync();
        await Search.FillAsync(key);
        await Expect(Search).ToHaveValueAsync(key);
    }

    // Searches the key and picks its option; returns once the trigger shows it.
    public async Task PickAsync(string key)
    {
        await SearchAsync(key);
        await Option(key).ClickAsync();
        await Expect(AnnotationTrigger).ToHaveTextAsync(key);
    }

    // Cancel; nothing opens.
    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
