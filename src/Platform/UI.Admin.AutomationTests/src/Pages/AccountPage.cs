using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /account (the user menu's "Account"): outside any project, so the navbar's title "Account" is the page's heading. The
// cards, top to bottom: the profile (name, e-mail, the account id with "Copy to clipboard", "Sign out"), "Memberships"
// (a link per organization and project of the user: its key, the kind "Organization" or "Project", the role badge; a
// project opens its default view's annotations), "Preferences" of this browser (Theme Light, Dark or System, Row
// density, Document format, Annotation editing, Date format, "Reset preferences") and the personal "Annotation types"
// ("Reset types"). Preferences apply at once, without a Save and without a toast, and stay in localStorage, so a test's
// fresh browser context starts from the defaults. Sections add the cards' controls in AccountPage.<Section>.cs.
public sealed partial class AccountPage(App app, AppRoutes routes)
{
    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Account", Exact = true, Level = 1 });

    // The profile card's button; the user menu has a menu item of the same name.
    public ILocator SignOutButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true });

    // The copy button of the account id in the profile card.
    public ILocator CopyAccountIdButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Copy to clipboard", Exact = true });

    public ILocator MembershipsCard => Card("Memberships");

    // The links of the memberships, one per organization and project.
    public ILocator Memberships => MembershipsCard.GetByRole(AriaRole.Link);

    public ILocator PreferencesCard => Card("Preferences");

    public ILocator AnnotationTypesCard => Card("Annotation types");

    // The membership of an organization ("acme") or a project ("acme.billing"), by its exact key.
    public ILocator Membership(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Memberships.Filter(new() { Has = app.Page.GetByText(key, new() { Exact = true }) });
    }

    // A tab of the Theme control of Preferences: "Light", "Dark" or "System"; the selected one has aria-selected="true".
    public ILocator ThemeTab(string theme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        return PreferencesCard.GetByRole(AriaRole.Tab, new() { Name = theme, Exact = true });
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Account);
        await app.WaitForReadyAsync();
        await Expect(Heading).ToBeVisibleAsync();
    }

    // Picks the theme ("Light", "Dark" or "System") and waits until its tab is the selected one.
    public async Task SetThemeAsync(string theme)
    {
        var tab = ThemeTab(theme);
        await tab.ClickAsync();
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");
    }

    // A card of the page by its heading (h2).
    private ILocator Card(string heading)
    {
        return NuxtLocators.Card(app.Page.Locator(":root"), heading, 2);
    }
}
