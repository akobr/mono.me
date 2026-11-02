using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

// Context Switcher: the organization, project and view buttons of the navigation "Context". Switching keeps the
// section; the menus are checked for the items they must include, never as exact lists, because a project can have
// more views and an organization more projects.
[Area(Area.Navigation)]
public sealed partial class NavigationContextSwitcherTests : PlatformTest
{
    // NAV-CTX-01: The view switcher changes the view, keeps the section and marks the default view.
    // From a generated, never registered view, the view menu offers the default view ("default prod") and "Manage
    // views"; the default view keeps the section (Configurations, and Modeling with its tab) and is marked "prod" on the
    // button and in the menu's label. The view button is hidden below 640 px, hence the desktop only.
    [Test]
    [TestCase(66)]
    [DesktopOnly]
    public async Task ContextSwitcher_PickView_KeepsSection()
    {
        var view = Routes with { View = PlatformData.ViewName() };
        var defaultView = Routes with { View = AppRoutes.DefaultView };
        var switcher = new ContextSwitcher(App, view);

        await App.GotoAsync(view.Configurations);
        await App.WaitForReadyAsync();
        await Expect(switcher.ViewButton).ToHaveAccessibleNameAsync(view.View);
        var menu = await App.MenuAsync();
        var label = App.NavigationViewLabel(menu, view.View);
        await Expect(label).ToBeVisibleAsync();
        await Expect(label.GetByText("prod", new() { Exact = true })).ToHaveCountAsync(0);

        await switcher.OpenSwitcherMenuAsync(switcher.ViewButton);
        var defaultItem = switcher.MenuItem(DefaultViewName());
        await Expect(defaultItem).ToBeVisibleAsync();
        await Expect(switcher.MenuItem("Manage views")).ToBeVisibleAsync();
        if (Target.IsMock)
        {
            await Expect(switcher.MenuItem("staging")).ToBeVisibleAsync();
        }

        await switcher.PickOpenMenuItemAsync(defaultItem);
        await App.ExpectAtAsync(defaultView.Configurations);
        await Expect(switcher.ViewButton).ToHaveAccessibleNameAsync(DefaultViewName());
        menu = await App.MenuAsync();
        await Expect(App.NavigationViewLabel(menu, AppRoutes.DefaultView).GetByText("prod", new() { Exact = true })).ToBeVisibleAsync();

        // Only the URL: a live Reader sees "Contributors only" there, and the tab stays in the URL all the same.
        await App.GotoAsync(view.ModelingTab("templates"));
        await App.WaitForReadyAsync();
        await switcher.PickViewAsync(AppRoutes.DefaultView);
        await App.ExpectAtAsync(defaultView.ModelingTab("templates"));

        await switcher.OpenSwitcherMenuAsync(switcher.ViewButton);
        await switcher.PickOpenMenuItemAsync(switcher.MenuItem("Manage views"));
        await App.ExpectAtAsync(Routes.Views);
        await Expect(new ViewsPage(App, Routes).View(AppRoutes.DefaultView)).ToBeVisibleAsync();
    }

    // NAV-CTX-02: The project switcher keeps the project section and the view section.
    // With a second project of the organization (a disposable one), the project menu lists both and "Organization
    // overview"; picking a project keeps a project section (Settings) as it is and opens a view section (Values store)
    // in the other project's default view.
    [Test]
    [TestCase(68)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task ContextSwitcher_PickProject_KeepsSection()
    {
        var other = await CreateDisposableProjectAsync();
        var switcher = new ContextSwitcher(App, Routes);

        await new ProjectSettingsPage(App, Routes).OpenAsync();
        await Expect(switcher.ProjectButton).ToHaveAccessibleNameAsync(Routes.Project);

        await switcher.OpenSwitcherMenuAsync(switcher.ProjectButton);
        await Expect(switcher.MenuItem(Routes.Project)).ToBeVisibleAsync();
        await Expect(switcher.MenuItem(other.Project)).ToBeVisibleAsync();
        await Expect(switcher.MenuItem("Organization overview")).ToBeVisibleAsync();
        if (Target.IsMock)
        {
            await Expect(switcher.MenuItem("shipping")).ToBeVisibleAsync();
        }

        await switcher.PickOpenMenuItemAsync(switcher.MenuItem(other.Project));
        await App.ExpectAtAsync(other.Settings());

        await App.NavigateAsync("Values store");
        await App.ExpectAtAsync(other.KeyValues);

        // On a phone the Values store's navbar actions cover most of the long name of the disposable project.
        await switcher.OpenSwitcherMenuAsync(switcher.ProjectButton);
        await switcher.PickOpenMenuItemAsync(switcher.MenuItem(Routes.Project));
        await App.ExpectAtAsync((Routes with { View = AppRoutes.DefaultView }).KeyValues);

        await switcher.OpenSwitcherMenuAsync(switcher.ProjectButton);
        await switcher.PickOpenMenuItemAsync(switcher.MenuItem("Organization overview"));
        await App.ExpectAtAsync(Routes.OrganizationOverview);
        await Expect(new OrganizationOverview(App, Routes).Heading).ToBeVisibleAsync();
    }

    // NAV-CTX-03: The organization switcher lists the organizations and opens an overview.
    // The organization menu lists the user's organizations and "All organizations", which leaves the context; back in
    // the view, the organization's item opens its overview, whose context has only the organization.
    [Test]
    [TestCase(70)]
    public async Task ContextSwitcher_PickOrganization_OpensOverview()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var switcher = new ContextSwitcher(App, Routes);
        await Expect(switcher.OrganizationButton).ToHaveAccessibleNameAsync(Routes.Organization);

        await switcher.OpenSwitcherMenuAsync(switcher.OrganizationButton);
        await Expect(switcher.MenuItem(Routes.Organization)).ToBeVisibleAsync();
        await Expect(switcher.MenuItem("All organizations")).ToBeVisibleAsync();
        if (Target.IsMock)
        {
            await Expect(switcher.MenuItem("northwind")).ToBeVisibleAsync();
        }

        await switcher.PickOpenMenuItemAsync(switcher.MenuItem("All organizations"));
        await App.ExpectAtAsync(Routes.Organizations);
        await Expect(new OrganizationsPage(App, Routes).Heading).ToBeVisibleAsync();
        await Expect(switcher.Root).ToHaveCountAsync(0);

        await Page.GoBackAsync();
        await App.ExpectAtAsync(Routes.Annotations);
        await Expect(annotations.Filter).ToBeVisibleAsync();
        await Expect(switcher.OrganizationButton).ToHaveAccessibleNameAsync(Routes.Organization);
        await Expect(switcher.ProjectButton).ToHaveAccessibleNameAsync(Routes.Project);

        await switcher.PickOrganizationAsync(Routes.Organization);
        await Expect(new OrganizationOverview(App, Routes).Heading).ToBeVisibleAsync();
        await Expect(switcher.Root.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);
    }

    // The default view's name in the view button and the view menu: its name, then the badge "prod".
    [GeneratedRegex(@"^default\s*prod$")]
    private static partial Regex DefaultViewName();
}
