using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// The onboarding wizard of Sign In and Account (ACC-SGN-06 to ACC-SGN-13): its steps Welcome, Residency, Security,
// Exhibition and Organization, the ways between them, its three ends and Sign out. Mock only, in the new-user scenario,
// where Ada Lovelace is signed in without an account: every pool user of a deployed target has one. Each test builds
// its own App, because the first navigation of PlatformTest.App would add the default scenario.
public sealed partial class SignInTests
{
    // The exhibition of the mock: the project exhibition/exhibition of Grace Hopper, with a small catalog, which any
    // user may join as a Reader.
    private static readonly AppRoutes Exhibition = AppRoutes.Parse("exhibition/exhibition");

    // ACC-SGN-06: A new user passes the onboarding steps and creates the first organization and project.
    // In the mock's new-user scenario Ada Lovelace has no account, so the app opens onboarding on its first step; Next
    // leads through Residency, Security and Exhibition to the organization form, which names the two agreements under
    // it, refuses empty and reserved names, then creates the organization and the project, whose card shows the trimmed
    // description.
    [Test]
    [TestCase(21)]
    [MockOnly("the new-user scenario, a signed-in user without an account")]
    public async Task Onboarding_NewUser_CreatesFirstOrganization()
    {
        var organization = PlatformData.OrganizationKey();
        var project = PlatformData.ProjectKey();
        var description = PlatformData.Description(200);
        var created = new AppRoutes(organization, project, AppRoutes.DefaultView);

        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        await app.GotoAsync(created.Home);
        var onboarding = new OnboardingPage(app, created);
        await onboarding.WaitForAsync();
        await Expect(onboarding.Heading).ToHaveTextAsync("Welcome, Ada Lovelace");
        await onboarding.ExpectStepsAsync("Step 1: Welcome", "Step 2: Residency", "Step 3: Security", "Step 4: Exhibition", "Step 5: Organization");
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 1: Welcome");
        await Expect(onboarding.StepHeading(OnboardingPage.WelcomeHeading)).ToBeVisibleAsync();

        await onboarding.NextAsync(OnboardingPage.ResidencyHeading);
        await onboarding.NextAsync(OnboardingPage.SecurityHeading);
        await onboarding.NextAsync(OnboardingPage.ExhibitionHeading);
        await onboarding.NextAsync(OnboardingPage.OrganizationHeading);
        await Expect(onboarding.Intro).ToBeVisibleAsync();
        await Expect(onboarding.CreateAndContinueButton).ToBeVisibleAsync();

        // Both ways to end create the account, so the agreements stand under the form.
        await Expect(onboarding.Agreements).ToHaveTextAsync(OnboardingPage.AgreementsText);
        await onboarding.ExpectLinksAsync(
            ("Customer License Agreement", "https://42for.software/legal/customer-license-agreement"),
            ("Data Processing Agreement", "https://42for.software/legal/data-processing-agreement"));

        await onboarding.SubmitAsync();
        await Expect(onboarding.FieldError("Enter a organization name.")).ToBeVisibleAsync();
        await Expect(onboarding.FieldError("Enter a project name.")).ToBeVisibleAsync();

        await onboarding.OrganizationField.FillAsync("invitations");
        await onboarding.SubmitAsync();
        await Expect(onboarding.FieldError("This organization name is reserved.")).ToBeVisibleAsync();

        await onboarding.CreateAsync(organization, project, $"  {description}  ");
        await app.ExpectAtAsync(created.Annotations);
        await Expect(new AnnotationsPage(app, created).EmptyState).ToBeVisibleAsync();

        var overview = new OrganizationOverview(app, created);
        await overview.OpenAsync();
        var card = overview.ProjectCard(project);
        await Expect(card.GetByText("Owner", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(card.GetByText($"{organization}.{project}", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(overview.ProjectCardDescription(project)).ToHaveTextAsync(description);
        await Expect(overview.ProjectCardDescription(project)).ToHaveAttributeAsync("title", description);
    }

    // ACC-SGN-07: The welcome and residency steps of onboarding introduce the platform, state the EU hosting and list
    // the sub-processors.
    // The first step meets the user with the platform, its motto, its pricing and a link to the documentation. The
    // second says in its heading and in bold that customer data, machine access and AI data processing stay in the EU.
    // Its list names the four sub-processors, each with its region and purpose; the entry of WorkOS says what it
    // receives and that 2S makes and records every authorization decision in the EU. Its two links open the cloud
    // hosting and the list of sub-processors in a new tab.
    [Test]
    [TestCase(492)]
    [MockOnly("the new-user scenario, a signed-in user without an account")]
    public async Task Onboarding_WelcomeAndResidency_StateEuHostingAndSubProcessors()
    {
        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, Routes);
        await onboarding.OpenAsync();
        await Expect(onboarding.StepHeading(OnboardingPage.WelcomeHeading)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.MottoText)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.PricingText)).ToBeVisibleAsync();
        await onboarding.ExpectLinksAsync(("Documentation", "https://42for.net/platform/introduction"));

        await onboarding.NextAsync(OnboardingPage.ResidencyHeading);
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 2: Residency");
        await Expect(onboarding.HostingLead).ToBeVisibleAsync();

        await Expect(onboarding.Topic("Sub-processors")).ToBeVisibleAsync();
        var subProcessors = new (string Name, string Region, string[] Texts)[]
        {
            ("Microsoft Azure", "EU only", ["Hosting, storage, secrets, logs"]),
            ("Sentry", "EU only", ["Error reporting"]),
            ("PostHog", "EU only", ["Product analytics"]),
            ("WorkOS", "USA", [OnboardingPage.WorkOsPurposeText, OnboardingPage.WorkOsNoteText]),
        };
        await Expect(onboarding.SubProcessors).ToHaveCountAsync(subProcessors.Length);
        for (var index = 0; index < subProcessors.Length; index++)
        {
            var (name, region, texts) = subProcessors[index];
            var entry = onboarding.SubProcessors.Nth(index);
            await Expect(entry.GetByText(name, new() { Exact = true })).ToBeVisibleAsync();
            await Expect(entry.GetByText(region, new() { Exact = true })).ToBeVisibleAsync();
            foreach (var text in texts)
            {
                await Expect(entry.GetByText(text, new() { Exact = true })).ToBeVisibleAsync();
            }
        }

        await onboarding.ExpectLinksAsync(
            ("Cloud hosting", "https://42for.net/platform/cloud"),
            ("Sub-processors list", "https://42for.software/legal/sub-processors"));
    }

    // ACC-SGN-08: The security step of onboarding explains secrets, machines, people and security testing and links to
    // the details.
    // The security step, opened by its address, has a lead and four sections in this order: Secrets, Machines and
    // pipelines, People (with the three safeguards and the way to an EU-only identity provider) and Security testing;
    // its three links open the security details, the authentication of machines and the self-hosting option in a new
    // tab.
    [Test]
    [TestCase(493)]
    [MockOnly("the new-user scenario, a signed-in user without an account")]
    public async Task Onboarding_Security_ExplainsSafeguardsAndLinksToDetails()
    {
        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, Routes);
        await onboarding.OpenAsync("security");
        await Expect(onboarding.StepHeading(OnboardingPage.SecurityHeading)).ToBeVisibleAsync();
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 3: Security");
        await Expect(onboarding.Text(OnboardingPage.SecurityLeadText)).ToBeVisibleAsync();

        await Expect(onboarding.Topics).ToHaveTextAsync(["Secrets", "Machines and pipelines", "People", "Security testing"]);
        await Expect(onboarding.Text(OnboardingPage.SecretsText)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.MachinesText)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.PeopleText)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.IdentityProviderText)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.SecurityTestingText)).ToBeVisibleAsync();

        await onboarding.ExpectLinksAsync(
            ("Security details", "https://42for.net/platform/security"),
            ("Authentication of machines", "https://42for.net/platform/auth-machine"),
            ("Self-hosting option", "https://42for.net/platform/self-hosting"));
    }

    // ACC-SGN-09: Back, Next, the step list and the browser's back move between the onboarding steps and keep what was
    // entered.
    // The first step has no Back. Next and Back move one step and name it in the address; the step list opens any
    // step. The choice of the exhibition is in the address too, and the organization form keeps a typed name over Back
    // and Next. The browser's back returns to the exhibition step with its box checked, and so does a reload.
    [Test]
    [TestCase(494)]
    [MockOnly("the new-user scenario, a signed-in user without an account")]
    public async Task Onboarding_Steps_MoveBackAndForthAndKeepInput()
    {
        var organization = PlatformData.OrganizationKey();

        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, Routes);
        await onboarding.OpenAsync();
        await Expect(onboarding.StepHeading(OnboardingPage.WelcomeHeading)).ToBeVisibleAsync();
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 1: Welcome");

        // The Back of the form is in the page, hidden with its step.
        await Expect(onboarding.BackButton).ToBeHiddenAsync();
        await app.ExpectQueryAsync("step", null);

        await onboarding.NextAsync(OnboardingPage.ResidencyHeading);
        await app.ExpectAtAsync(Routes.Onboarding);
        await app.ExpectQueryAsync("step", "residency");
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 2: Residency");

        await onboarding.NextAsync(OnboardingPage.SecurityHeading);
        await app.ExpectQueryAsync("step", "security");
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 3: Security");

        await onboarding.BackAsync(OnboardingPage.ResidencyHeading);
        await app.ExpectQueryAsync("step", "residency");
        await onboarding.BackAsync(OnboardingPage.WelcomeHeading);
        await app.ExpectAtAsync(Routes.Onboarding);
        await app.ExpectQueryAsync("step", null);
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 1: Welcome");

        await onboarding.SelectStepAsync("Step 4: Exhibition", OnboardingPage.ExhibitionHeading);
        await Expect(onboarding.JoinExhibitionCheckbox).Not.ToBeCheckedAsync();
        await onboarding.ChooseExhibitionAsync();
        await app.ExpectQueryAsync("step", "exhibition");
        await app.ExpectQueryAsync("join", "1");

        await onboarding.SelectStepAsync("Step 5: Organization", OnboardingPage.OrganizationHeading);
        await onboarding.OrganizationField.FillAsync(organization);
        await onboarding.BackAsync(OnboardingPage.ExhibitionHeading);
        await Expect(onboarding.OrganizationField).ToBeHiddenAsync();
        await onboarding.NextAsync(OnboardingPage.OrganizationHeading);
        await Expect(onboarding.OrganizationField).ToHaveValueAsync(organization);
        await Expect(onboarding.JoiningExhibition).ToBeVisibleAsync();

        // The choice came with the step: both are in the address of the history entry.
        await Page.GoBackAsync();
        await Expect(onboarding.StepHeading(OnboardingPage.ExhibitionHeading)).ToBeVisibleAsync();
        await Expect(onboarding.JoinExhibitionCheckbox).ToBeCheckedAsync();

        await Page.ReloadAsync();
        await Expect(onboarding.StepHeading(OnboardingPage.ExhibitionHeading)).ToBeVisibleAsync();
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 4: Exhibition");
        await Expect(onboarding.JoinExhibitionCheckbox).ToBeCheckedAsync();
    }

    // ACC-SGN-10: A new user joins the exhibition with an own organization and opens it as a Reader.
    // The exhibition step tells the story of the demo and what it shows; its box "Show me the exhibition" is unchecked
    // at first and says what joining does. Checked, the last step says so; the form creates the organization and the
    // wizard ends in its project with a toast that links to the exhibition. /orgs lists the own organization as Owner
    // and the exhibition's as a project member with the Reader role, and the exhibition's catalog has annotations and no
    // create action.
    [Test]
    [TestCase(495)]
    [MockOnly("the new-user scenario, a signed-in user without an account, and the exhibition of the mock")]
    public async Task Onboarding_JoinExhibitionWithOrganization_OpensItAsReader()
    {
        var organization = PlatformData.OrganizationKey();
        var project = PlatformData.ProjectKey();
        var created = new AppRoutes(organization, project, AppRoutes.DefaultView);

        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, created);
        await onboarding.OpenAsync("exhibition");
        await Expect(onboarding.StepHeading(OnboardingPage.ExhibitionHeading)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.ExhibitionText)).ToBeVisibleAsync();
        await Expect(onboarding.Text(OnboardingPage.ExhibitionPromiseText)).ToBeVisibleAsync();
        await Expect(onboarding.JoinExhibitionCheckbox).Not.ToBeCheckedAsync();
        await Expect(onboarding.ExhibitionChoiceDescription).ToBeVisibleAsync();

        await onboarding.ChooseExhibitionAsync();
        await onboarding.NextAsync(OnboardingPage.OrganizationHeading);
        await Expect(onboarding.JoiningExhibition).ToBeVisibleAsync();
        await Expect(onboarding.ExhibitionOnlyButton).ToBeVisibleAsync();

        // The wizard ends in the own project, so the toast is the way to the exhibition; it stays 10 seconds.
        await onboarding.CreateAsync(organization, project);
        var joined = app.ToastItem("You joined the exhibition");
        await Expect(joined).ToBeVisibleAsync();
        await Expect(joined.GetByRole(AriaRole.Link, new() { Name = "Open the exhibition", Exact = true })).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Exhibition.Annotations));
        await app.ExpectAtAsync(created.Annotations);

        // The membership is the project's only: no role in the organization of the exhibition.
        var organizations = new OrganizationsPage(app, created);
        await organizations.OpenAsync();
        await Expect(organizations.OrganizationRoleBadge(organization, "Owner")).ToBeVisibleAsync();
        await Expect(organizations.OrganizationRoleBadge(Exhibition.Organization, "Project member")).ToBeVisibleAsync();
        var exhibitionLink = organizations.Project(Exhibition.Organization, Exhibition.Project);
        await Expect(exhibitionLink).ToHaveAccessibleNameAsync("exhibition Reader");
        await Expect(exhibitionLink).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Exhibition.Annotations));

        // A Reader browses the catalog and creates nothing.
        var catalog = new AnnotationsPage(app, Exhibition);
        await catalog.OpenAsync();
        await Expect(catalog.AnnotationRows.First).ToBeVisibleAsync();
        await catalog.FilterAsync("key:sbt.northwind");
        await Expect(catalog.Item("sbt.northwind")).ToBeVisibleAsync();
        await catalog.ExpectNoCreateActionsAsync();
    }

    // ACC-SGN-11: A new user joins only the exhibition and creates an organization later.
    // The last step offers "Continue with the exhibition only" once the exhibition is chosen. It joins without an
    // organization and opens the exhibition, with a toast that needs no link. The user has an account from then on, so
    // /onboarding opens the exhibition again, and the own organization comes from New organization on /orgs.
    [Test]
    [TestCase(496)]
    [MockOnly("the new-user scenario, a signed-in user without an account, and the exhibition of the mock")]
    public async Task Onboarding_JoinOnlyExhibition_CreatesOrganizationLater()
    {
        var organization = PlatformData.OrganizationKey();
        var project = PlatformData.ProjectKey();
        var created = new AppRoutes(organization, project, AppRoutes.DefaultView);

        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, created);
        await onboarding.OpenAsync("organization");
        await Expect(onboarding.StepHeading(OnboardingPage.OrganizationHeading)).ToBeVisibleAsync();

        // The second way to end comes with the choice.
        await Expect(onboarding.JoiningExhibition).ToBeHiddenAsync();
        await Expect(onboarding.ExhibitionOnlyButton).ToBeHiddenAsync();

        await onboarding.SelectStepAsync("Step 4: Exhibition", OnboardingPage.ExhibitionHeading);
        await onboarding.ChooseExhibitionAsync();
        await onboarding.NextAsync(OnboardingPage.OrganizationHeading);
        await Expect(onboarding.JoiningExhibition).ToBeVisibleAsync();
        await Expect(onboarding.ExhibitionOnlyButton).ToBeVisibleAsync();

        // The join created the account. The user lands in the exhibition, so the toast has no link to it.
        await onboarding.ContinueWithExhibitionOnlyAsync();
        await Expect(app.Toast("You joined the exhibition")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Open the exhibition", Exact = true })).ToHaveCountAsync(0);
        await app.ExpectAtAsync(Exhibition.Annotations);
        await app.WaitForReadyAsync();
        await Expect(app.UserMenu).ToHaveAccessibleNameAsync("User menu for Ada Lovelace");
        await new AnnotationsPage(app, Exhibition).WaitForCatalogAsync();

        // With an account, onboarding is over: the auth guard replaces it with "/", which opens the last context.
        await app.GotoAsync(created.Onboarding);
        await app.ExpectAtAsync(Exhibition.Annotations);
        await Expect(onboarding.StepList).ToHaveCountAsync(0);

        var organizations = new OrganizationsPage(app, created);
        await organizations.OpenAsync();
        await Expect(organizations.OrganizationRoleBadge(Exhibition.Organization, "Project member")).ToBeVisibleAsync();
        await organizations.OpenNewOrganizationAsync();
        await organizations.SubmitNewOrganizationAsync(organization, project);
        await app.ExpectAtAsync(created.Annotations);

        // A new load of /orgs reads the account again.
        await organizations.OpenAsync();
        await Expect(organizations.OrganizationRoleBadge(organization, "Owner")).ToBeVisibleAsync();
        await Expect(organizations.OrganizationRoleBadge(Exhibition.Organization, "Project member")).ToBeVisibleAsync();
    }

    // ACC-SGN-12: Onboarding has no exhibition step when the deployment has none.
    // The mock's exhibition is turned off after the first load of the wizard, which asks for it once per page load.
    // Opened again, the wizard has four steps and Next leads from Security to the organization form, without the
    // second way to end; the address of the exhibition step shows the first step.
    [Test]
    [TestCase(497)]
    [MockOnly("the new-user scenario, and the hook that turns the exhibition of the mock off")]
    public async Task Onboarding_NoExhibition_HasFourSteps()
    {
        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, Routes);
        await onboarding.OpenAsync();
        await Expect(onboarding.Steps).ToHaveCountAsync(5);

        await new MockApi(app, Routes).SetExhibitionAsync(null);
        await onboarding.OpenAsync();
        await onboarding.ExpectStepsAsync("Step 1: Welcome", "Step 2: Residency", "Step 3: Security", "Step 4: Organization");

        await onboarding.NextAsync(OnboardingPage.ResidencyHeading);
        await onboarding.NextAsync(OnboardingPage.SecurityHeading);
        await onboarding.NextAsync(OnboardingPage.OrganizationHeading);
        await app.ExpectAtAsync(Routes.Onboarding);
        await app.ExpectQueryAsync("step", "organization");
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 4: Organization");
        await Expect(onboarding.ExhibitionOnlyButton).ToBeHiddenAsync();

        // The address of the missing step is the first step.
        await onboarding.OpenAsync("exhibition");
        await Expect(onboarding.StepHeading(OnboardingPage.WelcomeHeading)).ToBeVisibleAsync();
        await Expect(onboarding.CurrentStep).ToHaveAccessibleNameAsync("Step 1: Welcome");
    }

    // ACC-SGN-13: Sign out on onboarding returns to the sign-in page.
    // Sign out is below the card on every step; from the security step it signs the user out and opens /login.
    [Test]
    [TestCase(498)]
    [MockOnly("the new-user scenario, a signed-in user without an account")]
    public async Task Onboarding_SignOut_ReturnsToSignIn()
    {
        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "new-user");
        var onboarding = new OnboardingPage(app, Routes);
        await onboarding.OpenAsync("security");
        await Expect(onboarding.StepHeading(OnboardingPage.SecurityHeading)).ToBeVisibleAsync();
        await Expect(onboarding.SignOutButton).ToBeVisibleAsync();

        await onboarding.SignOutButton.ClickAsync();
        await app.ExpectAtAsync(Routes.Login());
        await Expect(new LoginPage(app).Heading).ToBeVisibleAsync();
    }
}
