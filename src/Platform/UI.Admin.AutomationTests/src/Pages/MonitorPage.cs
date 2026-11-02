using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/monitor (monitor.vue), the usage and the secret audit trail of the project's key values,
// for Administrators and Owners (manageAccess). Its toolbar has the tabs "Usage" (the default: no tab in the URL; the
// UsageReport) and "Audit" (?tab=audit; the AuditTrail) on every width; the selected tab has aria-selected "true", and
// any other ?tab= value opens Usage. A tab switch replaces the route (Usage drops ?tab=) and keeps the rest of the
// query. Only the open tab is mounted, so the date inputs "From" and "To" of both tabs never meet. Everyone else sees
// the empty state "Administrators only" and no tabs. The navbar's title "Monitor" is hidden below 2xl, so the selected
// tab is the sign the page is there. The old links /usage and /audit redirect here (AppRoutes.OldProjectPage).
public sealed partial class MonitorPage(App app, AppRoutes routes)
{
    public const string UsageTabName = "Usage";

    public const string AuditTabName = "Audit";

    public const string AdministratorsOnlyTitle = "Administrators only";

    public const string AdministratorsOnlyDescription = "The usage and the audit trail of the project's key values are visible to its Administrators.";

    public ILocator UsageTab => app.Page.GetByRole(AriaRole.Tab, new() { Name = UsageTabName, Exact = true });

    public ILocator AuditTab => app.Page.GetByRole(AriaRole.Tab, new() { Name = AuditTabName, Exact = true });

    // Every tab of the page: none below Administrator.
    public ILocator Tabs => app.Page.GetByRole(AriaRole.Tab);

    // The empty state below Administrator, an h2 with its description.
    public ILocator AdministratorsOnly => app.Page.GetByRole(AriaRole.Heading, new() { Name = AdministratorsOnlyTitle, Exact = true, Level = 2 });

    public ILocator AdministratorsOnlyText => app.Page.GetByText(AdministratorsOnlyDescription, new() { Exact = true });

    public UsageReport Usage => new(app);

    public AuditTrail Audit => new(app, routes);

    // Opens Monitor at the tab (null or "usage": Usage, "audit": Audit) as an Administrator, a page load, and waits until
    // that tab is selected.
    public async Task OpenAsync(string? tab = null)
    {
        await app.GotoAsync(routes.Monitor(tab));
        await app.WaitForReadyAsync();
        await ExpectSelectedAsync(tab == "audit" ? AuditTabName : UsageTabName);
    }

    // The tab of the name ("Usage" or "Audit") is the selected one, and the other is not.
    public async Task ExpectSelectedAsync(string tab)
    {
        var (selected, other) = tab switch
        {
            UsageTabName => (UsageTab, AuditTab),
            AuditTabName => (AuditTab, UsageTab),
            _ => throw new ArgumentException($"'{tab}' is not a tab of Monitor; use {UsageTabName} or {AuditTabName}.", nameof(tab)),
        };

        await Expect(selected).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(other).ToHaveAttributeAsync("aria-selected", "false");
    }

    // Clicks the tab "Audit" and waits for ?tab=audit and the selection.
    public async Task SelectAuditAsync()
    {
        await AuditTab.ClickAsync();
        await app.ExpectQueryAsync("tab", "audit");
        await ExpectSelectedAsync(AuditTabName);
    }

    // Clicks the tab "Usage" and waits until ?tab= is gone and Usage is selected.
    public async Task SelectUsageAsync()
    {
        await UsageTab.ClickAsync();
        await app.ExpectQueryAsync("tab", null);
        await ExpectSelectedAsync(UsageTabName);
    }
}
