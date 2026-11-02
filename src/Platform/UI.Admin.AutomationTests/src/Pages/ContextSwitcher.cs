using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The navbar's breadcrumb of the context (components/shell/ContextSwitcher.vue): the navigation "Context" with up to
// three menu buttons named by their values, the organization, the project and the view (the default view's is named
// "default prod", for its badge). The view button is hidden below sm (640 px): a phone cannot switch views here.
// - The organization menu lists the user's organizations, then "All organizations" (/orgs); an organization opens its
//   overview.
// - The project menu lists the user's projects of the organization, then "Organization overview"; a project keeps the
//   section: a view section (annotations, configurations, values store, graph, modeling with its tab) opens in its
//   default view without the detail, a project section (views, members, machines, settings, monitor) stays, and any
//   other page opens the default view's annotations.
// - The view menu lists the project's views, then "Manage views"; a view keeps the view section without the detail.
// While a menu is open or closing, reka-ui hides the rest of the page from the accessibility tree, so the pick helpers
// wait for the new URL and for the menu to be gone. A test that checks a menu's items before it picks one opens it with
// OpenSwitcherMenuAsync and picks with PickOpenMenuItemAsync.
public sealed class ContextSwitcher(App app, AppRoutes routes)
{
    public ILocator Root => app.Page.GetByRole(AriaRole.Navigation, new() { Name = "Context", Exact = true });

    public ILocator OrganizationButton => Root.GetByRole(AriaRole.Button).Nth(0);

    public ILocator ProjectButton => Root.GetByRole(AriaRole.Button).Nth(1);

    // Hidden below sm (640 px).
    public ILocator ViewButton => Root.GetByRole(AriaRole.Button).Nth(2);

    // The open menu.
    public ILocator Menu => app.Page.GetByRole(AriaRole.Menu);

    // An item of the open menu by its exact name ("acme", "billing", "staging", "All organizations", "Manage views").
    public ILocator MenuItem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });
    }

    public ILocator MenuItem(Regex name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { NameRegex = name });
    }

    // The item of a view in the open view menu: the default view's name ends in its "prod" badge.
    public ILocator ViewItem(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return MenuItem(new Regex($@"^{Regex.Escape(view)}(?:\s*prod)?$"));
    }

    // Switches to another organization of the user; returns on its overview (/<organization>).
    public async Task PickOrganizationAsync(string organization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);

        await OpenSwitcherMenuAsync(OrganizationButton);
        await PickOpenMenuItemAsync(MenuItem(organization));
        await app.ExpectAtAsync(organization);
    }

    // Switches to another project of the organization of the routes; returns once the URL is in that project (where in
    // it depends on the section, see the class comment).
    public async Task PickProjectAsync(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        await OpenSwitcherMenuAsync(ProjectButton);
        await PickOpenMenuItemAsync(MenuItem(project));
        await Expect(app.Page).ToHaveURLAsync(new Regex($@"/{Regex.Escape(routes.Organization)}/{Regex.Escape(project)}(?:[/?#].*)?$"));
    }

    // Switches to another view of the project of the routes (not on a phone: the view button is hidden there); returns
    // once the URL is in that view.
    public async Task PickViewAsync(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);

        await OpenSwitcherMenuAsync(ViewButton);
        await PickOpenMenuItemAsync(ViewItem(view));
        await Expect(app.Page).ToHaveURLAsync(new Regex($@"/{Regex.Escape(routes.Organization)}/{Regex.Escape(routes.Project)}/{Regex.Escape(view)}(?:[/?#].*)?$"));
    }

    // Opens the menu of a button of the switcher (OrganizationButton, ProjectButton or ViewButton) and returns it. The
    // button is clicked near its start: a long name is truncated, and on a phone the actions of a page's navbar (the
    // Values store's Export and Import) cover the rest of the button.
    public async Task<ILocator> OpenSwitcherMenuAsync(ILocator button)
    {
        ArgumentNullException.ThrowIfNull(button);

        await Expect(button).ToBeVisibleAsync();
        var box = await button.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The button of the context switcher has no box.");
        await button.ClickAsync(new() { Position = new() { X = 8, Y = box.Height / 2 } });
        await Expect(Menu).ToBeVisibleAsync();
        return Menu;
    }

    // Picks an item of the open menu (OpenSwitcherMenuAsync) and waits for the menu to close; the caller checks where
    // the item led, before any role query on the page (reka-ui hides the page while a menu closes).
    public async Task PickOpenMenuItemAsync(ILocator item)
    {
        ArgumentNullException.ThrowIfNull(item);

        await Expect(Menu).ToBeVisibleAsync();
        await item.ClickAsync();
        await Expect(Menu).ToBeHiddenAsync();
    }
}
