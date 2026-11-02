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
// wait for the new URL and for the menu to be gone.
public sealed partial class ContextSwitcher(App app, AppRoutes routes)
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

        await OrganizationButton.ClickAsync();
        await PickAsync(MenuItem(organization));
        await app.ExpectAtAsync(organization);
    }

    // Switches to another project of the organization of the routes; returns once the URL is in that project (where in
    // it depends on the section, see the class comment).
    public async Task PickProjectAsync(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        await ProjectButton.ClickAsync();
        await PickAsync(MenuItem(project));
        await Expect(app.Page).ToHaveURLAsync(new Regex($@"/{Regex.Escape(routes.Organization)}/{Regex.Escape(project)}(?:[/?#].*)?$"));
    }

    // Switches to another view of the project of the routes (not on a phone: the view button is hidden there); returns
    // once the URL is in that view.
    public async Task PickViewAsync(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);

        await ViewButton.ClickAsync();
        await PickAsync(ViewItem(view));
        await Expect(app.Page).ToHaveURLAsync(new Regex($@"/{Regex.Escape(routes.Organization)}/{Regex.Escape(routes.Project)}/{Regex.Escape(view)}(?:[/?#].*)?$"));
    }

    private async Task PickAsync(ILocator item)
    {
        await Expect(Menu).ToBeVisibleAsync();
        await item.ClickAsync();
        await Expect(Menu).ToBeHiddenAsync();
    }
}
