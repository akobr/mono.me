using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The admin UI's shell around every page. The user menu ("User menu for <name>") renders once the first route resolved
// and the user is signed in, so it is the sign that the app is ready. Below the lg breakpoint (1024 px) the sidebar is
// a slideover behind "Open navigation"; between lg and xl (1024 to 1279 px) it is docked but starts as an icon rail,
// whose links have no accessible name until "Expand sidebar" widens it; below sm (640 px) some actions of the navbar
// move to the command palette.
// Every page load of a test goes through GotoAsync, relative to Target:BaseUrl (links, NavigateAsync and palette
// actions route inside the loaded app); the first one carries ?mock-scenario=<name> when Target:MockScenario is set,
// the later ones keep the tab's mock data.
public sealed partial class App
{
    // The sidebar is docked from this width; below it, it opens from "Open navigation".
    public const int DockedSidebarWidth = 1024;

    // Below this width (the xl breakpoint) the docked sidebar starts as an icon rail.
    public const int ExpandedSidebarWidth = 1280;

    // Below this width (the sm breakpoint) the app has its phone layout.
    public const int PhoneWidth = 640;

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

    // The docked sidebar: the nav with the Organizations link.
    public ILocator Sidebar => Page.Locator("nav").Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true }) });

    // In the footer of the icon rail; "Collapse sidebar" once it is expanded.
    public ILocator ExpandSidebarButton => Page.GetByRole(AriaRole.Button, new() { Name = "Expand sidebar", Exact = true });

    public ILocator SearchButton => Page.GetByRole(AriaRole.Button, new() { Name = "Search…", Exact = true });

    public ILocator CommandPalette => Page.GetByRole(AriaRole.Dialog, new() { Name = "Search…", Exact = true });

    public ILocator CommandInput => CommandPalette.GetByPlaceholder("Search annotations, pages, projects and actions…");

    // The toaster (bottom right). While a modal is open the rest of the page is aria-hidden, so the region is found
    // with IncludeHidden and the toast by its text; a toast closes after about 5 seconds, so assert it right after the
    // action.
    public ILocator Notifications => Page.GetByRole(AriaRole.Region, new() { Name = "Notifications (F8)", Exact = true, IncludeHidden = true });

    // Shown instead of the page of an organization or a project the user has no role in (or that does not exist: the
    // app tells them apart only by the user's memberships).
    public ILocator NoAccessHeading => Page.GetByRole(AriaRole.Heading, new() { Name = "You have no access here", Exact = true });

    public bool HasDockedSidebar => Page.ViewportSize is not { Width: < DockedSidebarWidth };

    public bool IsPhone => Page.ViewportSize is { Width: < PhoneWidth };

    public static ILocator UserMenuOf(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.GetByRole(AriaRole.Button, new() { NameRegex = UserMenuName() });
    }

    public ILocator Toast(string title)
    {
        return Notifications.GetByText(title, new() { Exact = true });
    }

    public ILocator Toast(Regex title)
    {
        return Notifications.GetByText(title);
    }

    // path: a path of the app without the base, e.g. "orgs" or "acme/billing/default/annotations" (AppRoutes).
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

    // The current page is the path (AppRoutes), whatever its query and hash.
    public async Task ExpectAtAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        await Expect(Page).ToHaveURLAsync(new Regex($"/{Regex.Escape(path.Trim('/'))}/?(?:[?#].*)?$"));
    }

    // Opens the slideover of the sidebar on a narrow viewport and returns it; its links navigate and close it.
    public async Task<ILocator> OpenNavigationAsync()
    {
        await OpenNavigationButton.ClickAsync();
        await Expect(NavigationDialog).ToBeVisibleAsync();
        return NavigationDialog;
    }

    // Widens the docked sidebar when it is the icon rail (1024 to 1279 px, or collapsed with "["); nothing otherwise.
    public async Task ExpandSidebarAsync()
    {
        if (HasDockedSidebar && await ExpandSidebarButton.IsVisibleAsync())
        {
            await ExpandSidebarButton.ClickAsync();
            await Expect(Sidebar).ToBeVisibleAsync();
        }
    }

    // Follows a link of the navigation by its name ("Configurations", "Values store", "Settings", …): in the docked
    // sidebar (expanded first when it is the icon rail), or in the slideover behind "Open navigation", which closes
    // once the route changed. Two links are named "Members"; the first is the project's.
    public async Task NavigateAsync(string link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        if (HasDockedSidebar)
        {
            await ExpandSidebarAsync();
            await Sidebar.GetByRole(AriaRole.Link, new() { Name = link, Exact = true }).First.ClickAsync();
            return;
        }

        var navigation = await OpenNavigationAsync();
        await navigation.GetByRole(AriaRole.Link, new() { Name = link, Exact = true }).First.ClickAsync();
        await Expect(navigation).ToBeHiddenAsync();
    }

    // Runs an action of the command palette (search box "Search…", or ControlOrMeta+K): types its name and picks the
    // option. An option's accessible name ends in its group (e.g. "SettingsProject"), so the name is matched at the
    // start. The palette filters its options about 100 ms after the input: until then the unfiltered list is shown and
    // its options move under the pointer, so the option is clicked once it is the first, the best match.
    public async Task RunCommandAsync(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        await SearchButton.ClickAsync();
        await CommandInput.FillAsync(command);
        var best = CommandPalette.GetByRole(AriaRole.Option).First;
        await Expect(best).ToHaveAccessibleNameAsync(new Regex($"^{Regex.Escape(command)}"));
        await best.ClickAsync();
        await Expect(CommandPalette).ToBeHiddenAsync();
    }

    // The href of every link of the page outside the navigation (the sidebar and its slideover are nav elements), for a
    // page object that lists what the page links to.
    internal async Task<IReadOnlyList<string>> ContentLinksAsync()
    {
        return await Page.Locator("a[href]").EvaluateAllAsync<string[]>("links => links.filter(link => !link.closest('nav')).map(link => link.getAttribute('href') ?? '')");
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
