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
// the later ones keep the tab's mock data. Sections add their own members in App.<Section>.cs.
public sealed partial class App
{
    // The sidebar is docked from this width; below it, it opens from "Open navigation".
    public const int DockedSidebarWidth = 1024;

    // Below this width (the xl breakpoint) the docked sidebar starts as an icon rail.
    public const int ExpandedSidebarWidth = 1280;

    // Below this width (the sm breakpoint) the app has its phone layout.
    public const int PhoneWidth = 640;

    private const string UserMenuPrefix = "User menu for ";

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

    // The slideover of the sidebar: "Open sidebar" while the sidebar is pinned (below 1024 px), "Navigation" once it is
    // unpinned (at every width). Not any dialog: a dialog of the page must not pass for it.
    public ILocator NavigationDialog => Page.GetByRole(AriaRole.Dialog, new() { NameRegex = NavigationDialogName() });

    // The docked sidebar: the nav with the Organizations link.
    public ILocator Sidebar => Page.Locator("nav").Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true }) });

    // In the footer of the icon rail; "Collapse sidebar" once it is expanded.
    public ILocator ExpandSidebarButton => Page.GetByRole(AriaRole.Button, new() { Name = "Expand sidebar", Exact = true });

    public ILocator SearchButton => Page.GetByRole(AriaRole.Button, new() { Name = "Search…", Exact = true });

    public ILocator CommandPalette => Page.GetByRole(AriaRole.Dialog, new() { Name = "Search…", Exact = true });

    public ILocator CommandInput => CommandPalette.GetByPlaceholder("Search annotations, pages, projects and actions…");

    // The open user menu: Account, Organizations, Keyboard shortcuts and Sign out.
    public ILocator UserMenuList => Page.GetByRole(AriaRole.Menu);

    // The toaster (bottom right). While a modal is open the rest of the page is aria-hidden, so the region is found
    // with IncludeHidden and the toast by its text; a toast closes after about 5 seconds, so assert it right after the
    // action.
    public ILocator Notifications => Page.GetByRole(AriaRole.Region, new() { Name = "Notifications (F8)", Exact = true, IncludeHidden = true });

    // Every toast on screen (an li of the toaster); none while nothing was announced.
    public ILocator ShownToasts => Notifications.Locator("li");

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

    // The newest toast of the title: the same title can be stacked (two saves within 5 seconds), and the newest is the
    // one the last action showed.
    public ILocator Toast(string title)
    {
        return Notifications.GetByText(title, new() { Exact = true }).Last;
    }

    public ILocator Toast(Regex title)
    {
        return Notifications.GetByText(title).Last;
    }

    // The newest whole toast (its li: the title, the description and the actions) that has the text, its title or its
    // description (e.g. the downloaded file's name).
    public ILocator ToastItem(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return ShownToasts.Filter(new() { Has = Page.GetByText(title, new() { Exact = true }) }).Last;
    }

    public ILocator ToastItem(Regex title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return ShownToasts.Filter(new() { Has = Page.GetByText(title) }).Last;
    }

    // The description of the newest toast of the title (Nuxt UI's description slot), e.g. the API's message under "The
    // recipient was not changed"; assert it right after the action, like the toast.
    public ILocator ToastDescriptionOf(string title)
    {
        return ToastItem(title).Locator("[data-slot='description']");
    }

    public ILocator ToastDescriptionOf(Regex title)
    {
        return ToastItem(title).Locator("[data-slot='description']");
    }

    // A button of the newest toast of the title, e.g. "Undo" of "<key> deleted" (a deletion's toast stays 10 seconds,
    // others about 5): click it right after the action.
    public ILocator ToastAction(string title, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return ToastItem(title).GetByRole(AriaRole.Button, new() { Name = action, Exact = true, IncludeHidden = true });
    }

    // An item of the open user menu (OpenUserMenuAsync) by the start of its name: an item's name may end in its
    // shortcut ("Keyboard shortcuts?").
    public ILocator UserMenuItem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex($"^{Regex.Escape(name)}") });
    }

    // The options of the open command palette (OpenPaletteAsync) whose name starts with the text. An option's name ends
    // in its group or its key without a space ("SettingsProject", "AnnotationsView · default"), so the start is matched.
    public ILocator PaletteOption(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return CommandPalette.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($"^{Regex.Escape(text)}") });
    }

    public ILocator PaletteOption(Regex name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return CommandPalette.GetByRole(AriaRole.Option, new() { NameRegex = name });
    }

    // Picks the option of a select (a Nuxt UI select or select menu: a button or a combobox that opens a listbox) by
    // its exact name and waits until the select shows it.
    public async Task PickOptionAsync(ILocator select, string option)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentException.ThrowIfNullOrWhiteSpace(option);

        await select.ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
        await Expect(select).ToHaveTextAsync(option);
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

    // The current page is the path (AppRoutes), whatever its query and hash; ExpectQueryAsync checks the query.
    public async Task ExpectAtAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        await Expect(Page).ToHaveURLAsync(new Regex($"/{Regex.Escape(path.Trim('/'))}/?(?:[?#].*)?$"));
    }

    // Waits until the query parameter of the current URL has the value (decoded, as URLSearchParams reads it), or is
    // absent for null; the other parameters (mock-scenario, …) do not matter. Waits up to Execution:ActionTimeoutMs.
    public async Task ExpectQueryAsync(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            await Page.WaitForFunctionAsync(
                "([name, value]) => new URLSearchParams(location.search).get(name) === value",
                new object?[] { name, value },
                new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            var expected = value is null ? "absent" : $"'{value}'";
            throw new TimeoutException($"The query parameter {name} of the URL did not become {expected}; the page is at {Page.Url}.", exception);
        }
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

    // The navigation menu, ready to use: the docked sidebar (expanded first when it is the icon rail), or the slideover
    // behind "Open navigation" below 1024 px (and at every width once the sidebar is unpinned); CloseMenuAsync closes
    // the slideover again.
    public async Task<ILocator> MenuAsync()
    {
        if (HasDockedSidebar && !await OpenNavigationButton.IsVisibleAsync())
        {
            await ExpandSidebarAsync();
            await Expect(Sidebar).ToBeVisibleAsync();
            return Sidebar;
        }

        return await NavigationDialog.IsVisibleAsync() ? NavigationDialog : await OpenNavigationAsync();
    }

    // Closes the slideover of the navigation with Escape when it is open; the docked sidebar stays.
    public async Task CloseMenuAsync()
    {
        if (await NavigationDialog.IsVisibleAsync())
        {
            await Page.Keyboard.PressAsync("Escape");
            await Expect(NavigationDialog).ToBeHiddenAsync();
        }
    }

    // Follows a link of the navigation by its name ("Configurations", "Values store", "Settings", …): in the docked
    // sidebar (expanded first when it is the icon rail), or in the slideover behind "Open navigation", which closes
    // once the route changed. Two links are named "Members"; the first is the project's.
    public async Task NavigateAsync(string link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        if (HasDockedSidebar && !await OpenNavigationButton.IsVisibleAsync())
        {
            await ExpandSidebarAsync();
            await Sidebar.GetByRole(AriaRole.Link, new() { Name = link, Exact = true }).First.ClickAsync();
            return;
        }

        var navigation = await OpenNavigationAsync();
        await navigation.GetByRole(AriaRole.Link, new() { Name = link, Exact = true }).First.ClickAsync();
        await Expect(navigation).ToBeHiddenAsync();
    }

    // The name of the signed-in user, from the user menu's "User menu for <name>" (the mock: Ada Lovelace; a deployed
    // target: the AuthKit first and last name, else the e-mail). The pool's e-mails are not the mock user's.
    public async Task<string> SignedInNameAsync()
    {
        await Expect(UserMenu).ToBeVisibleAsync();
        var label = await UserMenu.GetAttributeAsync("aria-label") ?? string.Empty;
        return label.StartsWith(UserMenuPrefix, StringComparison.Ordinal) ? label[UserMenuPrefix.Length..] : label;
    }

    // Opens the user menu of the navbar and returns it.
    public async Task<ILocator> OpenUserMenuAsync()
    {
        await UserMenu.ClickAsync();
        await Expect(UserMenuList).ToBeVisibleAsync();
        return UserMenuList;
    }

    // Signs out through the user menu; returns on /login (the mock signs out at once, AuthKit through its logout).
    public async Task SignOutAsync()
    {
        await OpenUserMenuAsync();
        await ClickSignOutAsync();
    }

    // Clicks "Sign out" of the open user menu and returns once the app shows /login on its own origin. AuthKit gets
    // there through the WorkOS logout, which returns to <origin>/login only when that URL is a Sign-out URI of the
    // target's WorkOS environment; otherwise WorkOS goes to the environment's default Sign-out URI, and without one it
    // keeps the browser on its error page (error.workos.com/user_management/app-homepage-url-not-found). A sign-out
    // that ends off the app's origin fails with a message that names the Sign-out URI to register instead of a
    // mismatch of the URL; a failure on the app's own origin keeps Playwright's message.
    public async Task ClickSignOutAsync()
    {
        var origin = OriginOf(Page.Url);
        await UserMenuItem("Sign out").ClickAsync();

        try
        {
            await Expect(Page).ToHaveURLAsync(origin is null ? LoginPath() : new Regex($"^{Regex.Escape(origin)}{LoginPath()}", RegexOptions.IgnoreCase));
        }
        catch (PlaywrightException exception) when (origin is not null && !string.Equals(OriginOf(Page.Url), origin, StringComparison.OrdinalIgnoreCase))
        {
            throw new PlaywrightException(
                $"The sign-out ended on {Page.Url} instead of {origin}/login: WorkOS returns there only when {origin}/login is a Sign-out URI of the target's WorkOS environment, and otherwise goes to the environment's default Sign-out URI or, without one, to its error page (\"AuthKit checklist\" in docs/Platform/UI.Admin.AutomationTests/automation-tests.md).",
                exception);
        }
    }

    // Opens the command palette with the navbar's "Search…" (there on every width) and returns its input.
    public async Task<ILocator> OpenPaletteAsync()
    {
        await SearchButton.ClickAsync();
        await Expect(CommandInput).ToBeVisibleAsync();
        return CommandInput;
    }

    // Runs an action of the command palette: types its name and picks the option. The palette filters its options
    // about 100 ms after the input: until then the unfiltered list is shown and its options move under the pointer, so
    // the option is clicked once it is the first, the best match, whose name starts with the command (PaletteOption).
    public async Task RunCommandAsync(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var input = await OpenPaletteAsync();
        await input.FillAsync(command);
        await PickFirstPaletteOptionAsync(new Regex($"^{Regex.Escape(command)}"));
    }

    // Picks the first option of the open command palette (OpenPaletteAsync, then the search filled) once its whole name
    // is the expected one: the palette filters about 100 ms after the input, and until then the unfiltered options move
    // under the pointer. Returns once the palette closed.
    public async Task PickFirstPaletteOptionAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await PickFirstPaletteOptionAsync(new Regex($"^{Regex.Escape(name)}$"));
    }

    // Picks the first option of the open command palette once its name matches.
    public async Task PickFirstPaletteOptionAsync(Regex name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var first = CommandPalette.GetByRole(AriaRole.Option).First;
        await Expect(first).ToHaveAccessibleNameAsync(name);
        await first.ClickAsync();
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
        return AppRoutes.WithQuery(path, (name, value));
    }

    // The scheme, host and port of an absolute URL with a host (http://localhost:5173), or null for any other URL
    // (about:blank, data:, a relative one).
    private static string? OriginOf(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Authority.Length > 0 ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    // Playwright translates a locator's regex to JavaScript: only IgnoreCase, Multiline and Singleline are allowed.
    [GeneratedRegex("^User menu for ")]
    private static partial Regex UserMenuName();

    [GeneratedRegex("^(Open sidebar|Navigation)$")]
    private static partial Regex NavigationDialogName();

    [GeneratedRegex(@"/login(?:[?#].*)?$")]
    private static partial Regex LoginPath();
}
