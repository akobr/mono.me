using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/<view>/key-values, the "Values store": the list with its search, "New key" and the detail
// of a key (…/key-values/<key>, a region named by the key) with the tab links Value and Versions. A key is
// dot-separated segments of letters, digits and '_' (no '-'), stored in lower case. A new key opens its detail
// without a toast. A public text value is in the detail's "Value" text box; a secret one is masked ("••••••••", titled
// "Masked") until "Reveal" (ContributorWithSecrets and up), which shows it for 60 seconds ("Masked again after 60
// seconds."). The list is a table from md (768 px), a list of buttons below; both list only the keys of the current
// filters (?q=, ?level=<API level>, ?state=Deleted).
public sealed partial class KeyValuesPage(App app, AppRoutes routes)
{
    // The table replaces the list from this width (the md breakpoint).
    public const int TableWidth = 768;

    // The text of a masked secret value.
    public const string MaskedValue = "••••••••";

    public ILocator Search => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Search", Exact = true });

    // In the navbar (icon-only on a phone, with the same name) and in the empty state of an empty store.
    public ILocator NewKeyButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New key", Exact = true }).First;

    public ILocator NewKeyDialog => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New key", Exact = true });

    // "Key*": required.
    public ILocator NewKeyName => NewKeyDialog.GetByRole(AriaRole.Textbox, new() { NameRegex = KeyName() });

    public ILocator NewKeyValue => NewKeyDialog.GetByRole(AriaRole.Textbox, new() { Name = "Value", Exact = true });

    // On: the project's default secret level ("Standard" unless Settings say otherwise); off: Public.
    public ILocator NewKeySecret => NewKeyDialog.GetByRole(AriaRole.Switch, new() { NameRegex = SecretName() });

    // The level select of the dialog: the levels the project allows (Public, Standard, HSM, BYOK, HYOK).
    public ILocator NewKeyLevel => NewKeyDialog.GetByRole(AriaRole.Combobox, new() { Name = "Protection level", Exact = true });

    public ILocator CreateButton => NewKeyDialog.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true });

    public ILocator VersionsTab => app.Page.GetByRole(AriaRole.Link, new() { Name = "Versions", Exact = true });

    // The row of the current version in the Versions table ("v1(current)" … View, Compare), the table with the column
    // "Created" (the list's table has "Expires" too, in the Deleted state).
    public ILocator CurrentVersion => app.Page
        .GetByRole(AriaRole.Table)
        .Filter(new() { Has = app.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Created", Exact = true }) })
        .GetByRole(AriaRole.Row)
        .Filter(new() { HasText = "(current)" });

    private bool HasTable => app.Page.ViewportSize is not { Width: < TableWidth };

    // The detail of the key, a region named by the key (in lower case, as the store keeps it).
    public ILocator Detail(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Region, new() { Name = key.ToLowerInvariant(), Exact = true });
    }

    // The row (table) or the item (list) of a key, by its exact key; only keys of the current filters are listed.
    public ILocator Item(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var keyText = app.Page.GetByText(key.ToLowerInvariant(), new() { Exact = true });
        return HasTable
            ? app.Page.GetByRole(AriaRole.Row).Filter(new() { Has = keyText })
            : app.Page.GetByRole(AriaRole.Listitem).Filter(new() { Has = keyText });
    }

    // The value of a term of the detail's metadata: "Level" (the level's badge), "Version" (v<n>), "Author", "Updated",
    // "Created", "Content type", "Binding".
    public ILocator DetailValue(string key, string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return Detail(key).Locator($"dt:text-is(\"{term}\") + dd");
    }

    // The level's badge of the key's detail: "Public", "Standard", "HSM", "BYOK", "HYOK" or "Reference"; the only
    // element of the Level with a title (the level's description), not "Change protection" beside it.
    public ILocator LevelBadge(string key)
    {
        return DetailValue(key, "Level").Locator("[title]").First;
    }

    // The value of a server-side secret in its detail: "••••••••" (titled "Masked") until Reveal, then the value. The
    // only code element of the detail outside its metadata (the binding is a code element in the metadata).
    public ILocator SecretValue(string key)
    {
        return Detail(key).Locator("xpath=.//code[not(ancestor::dl)]");
    }

    // Beside the masked SecretValue, for ContributorWithSecrets and up.
    public ILocator RevealButton(string key)
    {
        return Detail(key).GetByRole(AriaRole.Button, new() { Name = "Reveal", Exact = true });
    }

    // Instead of RevealButton while the value is revealed.
    public ILocator HideButton(string key)
    {
        return Detail(key).GetByRole(AriaRole.Button, new() { Name = "Hide", Exact = true });
    }

    public ILocator MaskedAgainHint(string key)
    {
        return Detail(key).GetByText("Masked again after 60 seconds.", new() { Exact = true });
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

    // Creates a server-side secret: the switch "Secret" (the project's default secret level), and the level of the
    // "Protection level" select when given (its label: "Standard", "HSM", "BYOK"); the dialog closes and the key's
    // detail opens with the level's badge and the masked value. Needs ContributorWithSecrets and up (a Contributor's
    // dialog offers only Public).
    public async Task CreateSecretAsync(string key, string value, string? level = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await NewKeyButton.ClickAsync();
        await Expect(NewKeyDialog).ToBeVisibleAsync();
        await NewKeyName.FillAsync(key);
        await NewKeySecret.SetCheckedAsync(true);
        if (level is not null)
        {
            await NewKeyLevel.ClickAsync();
            await app.Page.GetByRole(AriaRole.Option, new() { Name = level, Exact = true }).ClickAsync();
            await Expect(NewKeyLevel).ToContainTextAsync(level);
        }

        await NewKeyValue.FillAsync(value);
        await CreateButton.ClickAsync();
        await Expect(NewKeyDialog).ToBeHiddenAsync();
        await app.ExpectAtAsync(routes.KeyValue(key));
        await Expect(SecretValue(key)).ToHaveTextAsync(MaskedValue);
    }

    // Reveals the secret value in the key's detail and returns its locator once it shows something else than the mask
    // (for 60 seconds, "Masked again after 60 seconds."); a reveal is recorded in the audit trail.
    public async Task<ILocator> RevealAsync(string key)
    {
        await RevealButton(key).ClickAsync();
        await Expect(HideButton(key)).ToBeVisibleAsync();
        await Expect(SecretValue(key)).Not.ToHaveTextAsync(MaskedValue);
        return SecretValue(key);
    }

    public async Task OpenVersionsAsync(string key)
    {
        await VersionsTab.ClickAsync();
        await app.ExpectAtAsync($"{routes.KeyValue(key)}/versions");
    }

    [GeneratedRegex("^Key")]
    private static partial Regex KeyName();

    [GeneratedRegex("^Secret")]
    private static partial Regex SecretName();
}
