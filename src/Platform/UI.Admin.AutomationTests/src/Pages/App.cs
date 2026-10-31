using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The admin UI's shell around every page. The user menu ("User menu for <name>") renders once the first route resolved
// and the user is signed in, so it is the sign that the app is ready. Below the lg breakpoint (1024 px) the sidebar is
// a slideover behind "Open navigation". Every navigation of a test goes through GotoAsync, relative to Target:BaseUrl;
// the first one carries ?mock-scenario=<name> when Target:MockScenario is set.
public sealed partial class App
{
    // The sidebar is docked from this width; below it, it opens from "Open navigation".
    public const int DockedSidebarWidth = 1024;

    private readonly string? mockScenario;
    private bool navigated;

    public App(IPage page, string? mockScenario = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        Page = page;
        this.mockScenario = string.IsNullOrWhiteSpace(mockScenario) ? null : mockScenario;
    }

    public IPage Page { get; }

    public ILocator UserMenu => UserMenuOf(Page);

    public ILocator OpenNavigationButton => Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true });

    // The slideover of the sidebar (named "Open sidebar" while the sidebar is pinned, "Navigation" otherwise).
    public ILocator NavigationDialog => Page.GetByRole(AriaRole.Dialog);

    public bool HasDockedSidebar => Page.ViewportSize is not { Width: < DockedSidebarWidth };

    public static ILocator UserMenuOf(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.GetByRole(AriaRole.Button, new() { NameRegex = UserMenuName() });
    }

    // path: a path of the app without the base, e.g. "orgs" or "acme/billing/default/annotations".
    public async Task GotoAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var target = path.TrimStart('/');
        if (!navigated && mockScenario is not null)
        {
            target = WithQuery(target, "mock-scenario", mockScenario);
        }

        navigated = true;
        await Page.GotoAsync(target);
    }

    public async Task WaitForReadyAsync()
    {
        await Expect(UserMenu).ToBeVisibleAsync();
    }

    // Opens the slideover of the sidebar on a narrow viewport and returns it; its links navigate and close it.
    public async Task<ILocator> OpenNavigationAsync()
    {
        await OpenNavigationButton.ClickAsync();
        await Expect(NavigationDialog).ToBeVisibleAsync();
        return NavigationDialog;
    }

    // Adds name=value to the query of a path, before its #fragment.
    internal static string WithQuery(string path, string name, string value)
    {
        var hash = path.IndexOf('#', StringComparison.Ordinal);
        var (beforeHash, fragment) = hash < 0 ? (path, string.Empty) : (path[..hash], path[hash..]);
        var separator = beforeHash.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{beforeHash}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}{fragment}";
    }

    // Playwright translates a locator's regex to JavaScript: only IgnoreCase, Multiline and Singleline are allowed.
    [GeneratedRegex("^User menu for ")]
    private static partial Regex UserMenuName();
}
