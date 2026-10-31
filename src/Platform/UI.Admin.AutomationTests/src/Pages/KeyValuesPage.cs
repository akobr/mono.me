using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/<view>/key-values, the "Values store": the list with its search, "New key" and the detail
// of a key (…/key-values/<key>, a region named by the key) with the tab links Value and Versions. A key is
// dot-separated segments of letters, digits and '_' (no '-'), stored in lower case. A new key opens its detail
// without a toast. A public text value is in the detail's "Value" text box; a secret one is masked until "Reveal".
public sealed partial class KeyValuesPage(App app, AppRoutes routes)
{
    public ILocator Search => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Search", Exact = true });

    // In the navbar (icon-only on a phone, with the same name) and in the empty state of an empty store.
    public ILocator NewKeyButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New key", Exact = true }).First;

    public ILocator NewKeyDialog => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New key", Exact = true });

    // "Key*": required.
    public ILocator NewKeyName => NewKeyDialog.GetByRole(AriaRole.Textbox, new() { NameRegex = KeyName() });

    public ILocator NewKeyValue => NewKeyDialog.GetByRole(AriaRole.Textbox, new() { Name = "Value", Exact = true });

    public ILocator CreateButton => NewKeyDialog.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true });

    public ILocator VersionsTab => app.Page.GetByRole(AriaRole.Link, new() { Name = "Versions", Exact = true });

    // The row of the current version in the Versions table ("v1(current)" … View, Compare).
    public ILocator CurrentVersion => app.Page
        .GetByRole(AriaRole.Table)
        .Filter(new() { Has = app.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Expires", Exact = true }) })
        .GetByRole(AriaRole.Row)
        .Filter(new() { HasText = "(current)" });

    public ILocator Detail(string key)
    {
        return app.Page.GetByRole(AriaRole.Region, new() { Name = key.ToLowerInvariant(), Exact = true });
    }

    // The public text value in the detail of the key.
    public ILocator Value(string key)
    {
        return Detail(key).GetByRole(AriaRole.Textbox, new() { Name = "Value", Exact = true });
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.KeyValues);
        await app.WaitForReadyAsync();
        await Expect(Search).ToBeVisibleAsync();
    }

    // Creates a public text key value; the dialog closes and the key's detail opens.
    public async Task CreateAsync(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await NewKeyButton.ClickAsync();
        await Expect(NewKeyDialog).ToBeVisibleAsync();
        await NewKeyName.FillAsync(key);
        await NewKeyValue.FillAsync(value);
        await CreateButton.ClickAsync();
        await Expect(NewKeyDialog).ToBeHiddenAsync();
        await app.ExpectAtAsync(routes.KeyValue(key));
    }

    public async Task OpenVersionsAsync(string key)
    {
        await VersionsTab.ClickAsync();
        await app.ExpectAtAsync($"{routes.KeyValue(key)}/versions");
    }

    [GeneratedRegex("^Key")]
    private static partial Regex KeyName();
}
