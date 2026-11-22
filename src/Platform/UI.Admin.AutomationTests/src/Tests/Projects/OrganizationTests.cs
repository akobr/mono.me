using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Projects;

// Organizations: the Organizations page (/orgs) and its New organization dialog, the cards with their role badges and
// project links, the overview of a new organization, the validation of the organization form, and the offer of the
// exhibition with its join.
[Area(Area.Projects)]
public sealed partial class OrganizationTests : PlatformTest
{
    // The exhibition of the mock: the project exhibition/exhibition of Grace Hopper, with a small catalog, which any
    // user may join as a Reader.
    private static readonly AppRoutes Exhibition = AppRoutes.Parse("exhibition/exhibition");

    // ORG-ORG-01: New organization creates the organization with its first project and lists it.
    // Every user may create an organization. New organization on /orgs creates it with its first project and the
    // project's description and opens the project's empty annotations; /orgs then lists the organization and the
    // project with the Owner role, and the organization's overview shows the project's card with its key and the whole
    // description. The only test of the suite that creates an organization: neither the UI nor the API deletes one.
    [Test]
    [TestCase(8)]
    public async Task Organization_Create_ListsItWithFirstProject()
    {
        var organization = PlatformData.OrganizationKey();
        var project = PlatformData.ProjectKey();
        var description = PlatformData.Description(200);
        var created = new AppRoutes(organization, project, AppRoutes.DefaultView);

        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await organizations.OpenNewOrganizationAsync();
        await Expect(organizations.NewOrganizationMessage("You become the owner of the organization and its first project.")).ToBeVisibleAsync();
        await Expect(organizations.NewOrganizationName).ToBeVisibleAsync();
        await Expect(organizations.NewOrganizationFirstProject).ToBeVisibleAsync();
        await Expect(organizations.NewOrganizationDescription).ToBeVisibleAsync();

        await organizations.SubmitNewOrganizationAsync(organization, project, description);
        await Expect(App.Toast($"Organization {organization} created")).ToBeVisibleAsync();

        await App.ExpectAtAsync(created.Annotations);
        await Expect(new AnnotationsPage(App, created).EmptyState).ToBeVisibleAsync();

        // A new load of /orgs reads the account again.
        await organizations.OpenAsync();
        await Expect(organizations.Organization(organization)).ToBeVisibleAsync();
        await Expect(organizations.OrganizationRoleBadge(organization, "Owner")).ToBeVisibleAsync();
        await Expect(organizations.Project(organization, project)).ToHaveAccessibleNameAsync(new Regex($@"^{Regex.Escape(project)}\s*Owner$"));

        await organizations.Organization(organization).ClickAsync();
        await App.ExpectAtAsync(created.OrganizationOverview);
        var overview = new OrganizationOverview(App, created);
        await Expect(overview.Heading).ToBeVisibleAsync();
        await Expect(overview.HeadingBadge("Owner")).ToBeVisibleAsync();

        await Expect(overview.ProjectLink(project)).ToBeVisibleAsync();
        await Expect(overview.ProjectCardRoleBadge(project, "Owner")).ToBeVisibleAsync();
        await Expect(overview.ProjectCardKey(project)).ToBeVisibleAsync();

        // The description comes with the project list (GET v1/access/projects) after the cards; it is clamped to two
        // lines, and its title holds the whole text.
        await Expect(overview.ProjectCardDescription(project)).ToHaveAttributeAsync("title", description);
        await Expect(overview.ProjectCardDescription(project)).ToHaveTextAsync(description);

        await overview.ProjectLink(project).ClickAsync();
        await App.ExpectAtAsync(created.Annotations);
    }

    // ORG-ORG-02: New organization validates the names and rejects an existing project.
    // The client shows the first error of each field under it, after each submit: the empty names, a name against the
    // pattern, the reserved organization and project names and a description over 500 characters. The organization
    // and project of the target, whose Owner the user is, pass the client and the API refuses them as a conflict,
    // shown under Organization without a toast. Cancel creates nothing.
    [Test]
    [TestCase(23)]
    [UserRole(Role.Owner)]
    public async Task Organization_CreateInvalid_ShowsFieldErrors()
    {
        var organization = PlatformData.OrganizationKey();
        var project = PlatformData.ProjectKey();

        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await organizations.OpenNewOrganizationAsync();

        await organizations.CreateOrganizationButton.ClickAsync();
        await Expect(organizations.NewOrganizationName).ToHaveAccessibleDescriptionAsync("Enter a organization name.");
        await Expect(organizations.NewOrganizationFirstProject).ToHaveAccessibleDescriptionAsync("Enter a project name.");
        await Expect(organizations.NewOrganizationDialog).ToBeVisibleAsync();

        await organizations.NewOrganizationName.FillAsync("Bad_Name");
        await organizations.CreateOrganizationButton.ClickAsync();
        await Expect(organizations.NewOrganizationName).ToHaveAccessibleDescriptionAsync(NameRules.PatternMessage);

        await organizations.NewOrganizationName.FillAsync("orgs");
        await organizations.CreateOrganizationButton.ClickAsync();
        await Expect(organizations.NewOrganizationName).ToHaveAccessibleDescriptionAsync("This organization name is reserved.");

        await organizations.NewOrganizationName.FillAsync(organization);
        await organizations.NewOrganizationFirstProject.FillAsync("settings");
        await organizations.CreateOrganizationButton.ClickAsync();
        await Expect(organizations.NewOrganizationFirstProject).ToHaveAccessibleDescriptionAsync("This project name is reserved.");
        await Expect(organizations.NewOrganizationMessage("This organization name is reserved.")).ToHaveCountAsync(0);

        await organizations.NewOrganizationFirstProject.FillAsync(project);
        await organizations.NewOrganizationDescription.FillAsync(PlatformData.Text(501));
        await organizations.CreateOrganizationButton.ClickAsync();
        await Expect(organizations.NewOrganizationDescription).ToHaveAccessibleDescriptionAsync("At most 500 characters.");
        await Expect(organizations.NewOrganizationMessage("This project name is reserved.")).ToHaveCountAsync(0);

        // The target's own organization and project: the API answers 409 (the project exists), which the form shows
        // under Organization; nothing is created, so no toast follows.
        await organizations.NewOrganizationName.FillAsync(Routes.Organization);
        await organizations.NewOrganizationFirstProject.FillAsync(Routes.Project);
        await organizations.NewOrganizationDescription.FillAsync(string.Empty);
        await organizations.CreateOrganizationButton.ClickAsync();
        await Expect(organizations.NewOrganizationName).ToHaveAccessibleDescriptionAsync("This organization already exists, or you are not its owner.");
        await Expect(App.Notifications.GetByText(OrganizationCreatedToast())).ToHaveCountAsync(0);
        await Expect(organizations.NewOrganizationDialog).ToBeVisibleAsync();

        await organizations.NewOrganizationCancelButton.ClickAsync();
        await Expect(organizations.NewOrganizationDialog).ToBeHiddenAsync();
        await Expect(organizations.Organization(organization)).ToHaveCountAsync(0);
    }

    // ORG-ORG-03: A user with an account joins the exhibition from the Organizations page and opens it as a Reader.
    // In the mock's default scenario Ada Lovelace has an account and no role in the exhibition: the last card of /orgs
    // offers it with the wizard's words and "Join the exhibition", a button, so no organization 'exhibition' is listed.
    // The join opens the exhibition's annotations with a toast, where a Reader has no create action; /orgs then lists
    // the exhibition's organization as a project member's with the project link 'exhibition Reader', and offers nothing.
    [Test]
    [TestCase(510)]
    [MockOnly("the exhibition of the mock, which the default scenario's user is no member of")]
    public async Task Organization_JoinExhibition_OpensItAsReader()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await Expect(organizations.ExhibitionOfferHeading).ToBeVisibleAsync();
        await Expect(organizations.ExhibitionOfferText(OnboardingPage.ExhibitionText)).ToBeVisibleAsync();
        await Expect(organizations.ExhibitionOfferText(OnboardingPage.ExhibitionChoiceDescriptionText)).ToBeVisibleAsync();
        await Expect(organizations.JoinExhibitionButton).ToBeVisibleAsync();

        // The user's own organizations come first; the exhibition's is not one of them yet.
        await Expect(organizations.Organization(Routes.Organization)).ToBeVisibleAsync();
        await Expect(organizations.Organization(Exhibition.Organization)).ToHaveCountAsync(0);

        await organizations.JoinExhibitionAsync();
        await Expect(App.Toast(OrganizationsPage.JoinedExhibitionToast)).ToBeVisibleAsync();
        await App.ExpectAtAsync(Exhibition.Annotations);

        // A Reader browses the catalog and creates nothing; asserted once the catalog loaded.
        var catalog = new AnnotationsPage(App, Exhibition);
        await catalog.WaitForCatalogAsync();
        await Expect(catalog.AnnotationRows.First).ToBeVisibleAsync();
        await catalog.ExpectNoCreateActionsAsync();

        // A new load of /orgs reads the account again. The membership is the project's only: no role in the
        // organization of the exhibition.
        await organizations.OpenAsync();
        await Expect(organizations.OrganizationRoleBadge(Exhibition.Organization, "Project member")).ToBeVisibleAsync();
        var exhibitionLink = organizations.Project(Exhibition.Organization, Exhibition.Project);
        await Expect(exhibitionLink).ToHaveAccessibleNameAsync("exhibition Reader");
        await Expect(exhibitionLink).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Exhibition.Annotations));
        await Expect(organizations.JoinExhibitionButton).ToHaveCountAsync(0);
        await Expect(organizations.ExhibitionOffer).ToHaveCountAsync(0);
    }

    // ORG-ORG-04: The Organizations page offers the exhibition only to a user who is not its member, on a deployment
    // that has one.
    // The page asks for the exhibition once per page load, so each change of the mock is followed by a new load of
    // /orgs. Without an exhibition the page lists the user's organizations and offers nothing; with it again, the offer
    // is back. After a join behind the page's back (the mock API, as from another tab) the user is a member: the page
    // lists the project 'exhibition Reader' and offers nothing.
    [Test]
    [TestCase(511)]
    [MockOnly("the hook that turns the exhibition of the mock off and on, and a join through the mock API")]
    public async Task Organization_ExhibitionOffer_HiddenForMemberOrWithoutExhibition()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await Expect(organizations.JoinExhibitionButton).ToBeVisibleAsync();

        await MockApi.SetExhibitionAsync(null);
        await organizations.OpenAsync();
        await Expect(organizations.Organization(Routes.Organization)).ToBeVisibleAsync();
        await Expect(organizations.ExhibitionOfferHeading).ToHaveCountAsync(0);
        await Expect(organizations.JoinExhibitionButton).ToHaveCountAsync(0);

        await MockApi.SetExhibitionAsync((Exhibition.Organization, Exhibition.Project));
        await organizations.OpenAsync();
        await Expect(organizations.JoinExhibitionButton).ToBeVisibleAsync();

        var joined = await MockApi.JoinExhibitionAsync();
        await Assert.That(joined.Status).IsEqualTo(200);
        await organizations.OpenAsync();
        await Expect(organizations.Project(Exhibition.Organization, Exhibition.Project)).ToHaveAccessibleNameAsync("exhibition Reader");
        await Expect(organizations.ExhibitionOfferHeading).ToHaveCountAsync(0);
        await Expect(organizations.JoinExhibitionButton).ToHaveCountAsync(0);
    }

    [GeneratedRegex("^Organization .+ created$")]
    private static partial Regex OrganizationCreatedToast();
}
