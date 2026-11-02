using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

// Routing: the router's own pages ("Page not found" and "You have no access here"), "/" with the brand's link, the
// project's address and /onboarding, which open the last context (lastContext in the browser's preferences), the page
// memory that brings each section of a view back to where the user left it until a reload, and the old project
// addresses that redirect to the sections of the pages they were merged into. The tests open the target view first
// where the last context matters: on a live target the captured sign-in may remember an older one.
[Area(Area.Navigation)]
public sealed class NavigationRoutingTests : PlatformTest
{
    private const string NoAccessDescription = "Your account is not a member of this organization or project. Ask an administrator to invite you.";

    // NAV-RTE-01: An unknown address shows Page not found, and Go home opens the last view.
    // An address below the view that no route matches, and the view's own address without a section, show "Page not
    // found" with the path; the menu still offers the last view, and "Go home" opens its annotations.
    [Test]
    [TestCase(117)]
    public async Task Routing_UnknownAddress_ShowsPageNotFound()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await Expect(annotations.Filter).ToBeVisibleAsync();

        var unknown = $"{Routes.ViewRoot}/no-such-page";
        await App.GotoAsync(unknown);
        await Expect(App.NotFoundHeading).ToBeVisibleAsync();
        await Expect(Page.GetByText($"Nothing lives at /{unknown}.", new() { Exact = true })).ToBeVisibleAsync();

        // The not-found page has no context of its own; the menu completes it with the remembered one.
        var menu = await App.MenuAsync();
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Annotations", Exact = true })).ToHaveAttributeAsync("href", $"/{Routes.Annotations}");
        await App.CloseMenuAsync();

        await App.GotoAsync(Routes.ViewRoot);
        await Expect(App.NotFoundHeading).ToBeVisibleAsync();
        await Expect(Page.GetByText($"Nothing lives at /{Routes.ViewRoot}.", new() { Exact = true })).ToBeVisibleAsync();

        await App.GoHomeLink.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotations);
    }

    // NAV-RTE-02: An organization or a project outside the memberships shows No access.
    // A generated organization and a generated project of the target organization (neither exists, and the app cannot
    // tell that from a missing membership) show "You have no access here"; "Your organizations" leads to /orgs, and the
    // project menu of the forbidden context opens the user's own project in its default view.
    [Test]
    [TestCase(119)]
    public async Task Routing_ForeignContext_ShowsNoAccess()
    {
        var foreignOrganization = Routes with { Organization = PlatformData.OrganizationKey() };
        await App.GotoAsync(foreignOrganization.OrganizationOverview);
        await App.WaitForReadyAsync();
        await Expect(App.NoAccessHeading).ToBeVisibleAsync();
        await Expect(Page.GetByText(NoAccessDescription, new() { Exact = true })).ToBeVisibleAsync();

        await App.YourOrganizationsLink.ClickAsync();
        await App.ExpectAtAsync(Routes.Organizations);
        await Expect(new OrganizationsPage(App, Routes).Heading).ToBeVisibleAsync();

        var foreignProject = Routes with { Project = PlatformData.ProjectKey(), View = AppRoutes.DefaultView };
        await App.GotoAsync(foreignProject.Annotations);
        await Expect(App.NoAccessHeading).ToBeVisibleAsync();
        var context = new ContextSwitcher(App, foreignProject);
        await Expect(context.OrganizationButton).ToHaveTextAsync(Routes.Organization);
        await Expect(context.ProjectButton).ToHaveTextAsync(foreignProject.Project);

        // From a view section the project's item opens the same section in the project's default view.
        await context.OpenSwitcherMenuAsync(context.ProjectButton);
        await context.PickOpenMenuItemAsync(context.MenuItem(Routes.Project));
        var own = Routes with { View = AppRoutes.DefaultView };
        await App.ExpectAtAsync(own.Annotations);
        await Expect(new AnnotationsPage(App, own).Filter).ToBeVisibleAsync();
    }

    // NAV-RTE-03: Home, the project address and the app root open the last context.
    // The brand's "2S Admin home" opens the last view's annotations; the project's address opens its default view,
    // which becomes the last context; "/" (from the organizations, after a reload) and /onboarding of a user with an
    // account open that context again.
    [Test]
    [TestCase(120)]
    public async Task Routing_Home_OpensLastContext()
    {
        var configurations = new ConfigurationsPage(App, Routes);
        await configurations.OpenAsync();
        await Expect(configurations.Filter).ToBeVisibleAsync();

        // In the header of the docked sidebar; on a phone in the slideover, which the navigation closes.
        await App.MenuAsync();
        await App.HomeLink.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotations);

        var defaultView = Routes with { View = AppRoutes.DefaultView };
        await App.GotoAsync(Routes.ProjectRoot);
        await App.ExpectAtAsync(defaultView.Annotations);

        await App.NavigateAsync("Organizations");
        await App.ExpectAtAsync(Routes.Organizations);
        await App.GotoAsync(Routes.Home);
        await App.ExpectAtAsync(defaultView.Annotations);

        // The auth guard replaces /onboarding with "/" once the account loaded, and "/" opens the last context.
        await App.GotoAsync(Routes.Onboarding);
        await App.ExpectAtAsync(defaultView.Annotations);
    }

    // NAV-RTE-04: Each section of a view comes back with its own filter until a reload.
    // The annotations and the configurations keep their own filters (which match nothing) while the user moves between
    // the sections in the app, by the menu and by the go-to keys, also across the Values store, which is not
    // remembered; a reload forgets them, so the annotations open without a filter.
    [Test]
    [TestCase(121)]
    public async Task PageMemory_ReturnToSection_RestoresItsFilter()
    {
        var annotationsFilter = $"key:{PlatformData.Slug("a")}";
        var configurationsFilter = $"key:{PlatformData.Slug("c")}";
        var annotations = new AnnotationsPage(App, Routes);
        var configurations = new ConfigurationsPage(App, Routes);

        await annotations.OpenAsync();
        await annotations.FilterAsync(annotationsFilter);
        await App.ExpectAtAsync(Routes.Annotations);

        await App.NavigateAsync("Configurations");
        await App.ExpectAtAsync(Routes.Configurations);
        await configurations.FilterAsync(configurationsFilter);

        // The chain blurs the filter first: the app ignores its shortcuts in a text box.
        await App.PressShortcutAsync("g", "a");
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", annotationsFilter);
        await Expect(annotations.Filter).ToHaveValueAsync(annotationsFilter);

        await App.NavigateAsync("Configurations");
        await App.ExpectAtAsync(Routes.Configurations);
        await App.ExpectQueryAsync("q", configurationsFilter);
        await Expect(configurations.Filter).ToHaveValueAsync(configurationsFilter);

        await App.NavigateAsync("Values store");
        await App.ExpectAtAsync(Routes.KeyValues);
        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", annotationsFilter);
        await Expect(annotations.Filter).ToHaveValueAsync(annotationsFilter);

        // A page load starts the in-memory page memory again, and its first navigation is never redirected.
        await configurations.OpenAsync();
        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", null);
        await Expect(annotations.Filter).ToHaveValueAsync(string.Empty);
    }

    // NAV-RTE-05: Old project links open their new sections.
    // The old addresses /secrets, /certificates and /invitations of the project redirect to the sections of Settings,
    // Machines and Members that replaced them; each page scrolls its section into view and focuses it. The Shared
    // certificates are an Administrator's section.
    [Test]
    [TestCase(122)]
    [UserRole(Role.Admin)]
    public async Task Routing_OldProjectLinks_OpenTheirSections()
    {
        await App.GotoAsync(Routes.OldProjectPage("secrets"));
        await App.ExpectAtAsync(Routes.Settings());
        var settings = new ProjectSettingsPage(App, Routes);
        await App.ExpectHashLandedAsync(settings.SecretsSection, "secrets");
        await Expect(settings.SecretsHeading).ToBeVisibleAsync();
        await Expect(settings.AllowedLevelsCard).ToBeVisibleAsync();

        await App.GotoAsync(Routes.OldProjectPage("certificates"));
        await App.ExpectAtAsync(Routes.Machines);
        var certificates = new MachinesPage(App, Routes).SharedCertificates;
        await App.ExpectHashLandedAsync(certificates.Root, "shared-certificates");
        await Expect(certificates.Heading).ToBeVisibleAsync();

        // Disabled while the project's machine policy does not allow shared certificates, so only its presence counts.
        await Expect(certificates.IssueButton).ToBeVisibleAsync();

        await App.GotoAsync(Routes.OldProjectPage("invitations"));
        await App.ExpectAtAsync(Routes.Members);
        var members = new MembersPage(App, Routes);
        await App.ExpectHashLandedAsync(members.InvitationsSection, "invitations");
        await Expect(members.InvitationsHeading).ToBeVisibleAsync();
    }
}
