using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Sign In and Account: signing in and out, the login, callback and unsupported pages, the Account page and a new
// user's onboarding.
[Area(Area.Access)]
public sealed partial class SignInTests : PlatformTest
{
    // ACC-SGN-01: A signed-in user sees the organizations and the target project.
    // The context starts signed in (the cached sign-in of the pool user, or the mock's user): /orgs lists the default
    // organization with its default project.
    [Test]
    [Elementary]
    [TestCase(1)]
    public async Task SignIn_ShowsOrganizations()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();

        await Expect(organizations.Heading).ToBeVisibleAsync();
        await Expect(App.UserMenu).ToBeVisibleAsync();
        await Expect(organizations.Organization(Target.Organization)).ToBeVisibleAsync();
        await Expect(organizations.Project(Target.Organization, Target.Project)).ToBeVisibleAsync();
    }

    // ACC-SGN-02: Signing out shows the sign-in page, and Continue with AuthKit signs in again.
    // Sign out from the user menu lands on /login, signed out; Continue with AuthKit signs in again (the mock at once,
    // a deployed target on the AuthKit hosted page), through /callback, and '/' opens the last context, the target
    // view's annotations, as the same user.
    [Test]
    [TestCase(12)]
    public async Task SignIn_SignOutAndContinue_ReturnsToLastProject()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var name = await App.SignedInNameAsync();

        await App.OpenUserMenuAsync();
        foreach (var item in new[] { "Account", "Organizations", "Keyboard shortcuts", "Sign out" })
        {
            await Expect(App.UserMenuItem(item)).ToBeVisibleAsync();
        }

        await App.UserMenuItem("Sign out").ClickAsync();
        await App.ExpectAtAsync(Routes.Login());
        var login = new LoginPage(App);
        await Expect(login.Heading).ToBeVisibleAsync();
        await Expect(login.Intro).ToBeVisibleAsync();
        await Expect(login.ContinueWithAuthKit).ToBeVisibleAsync();
        await Expect(App.UserMenu).ToHaveCountAsync(0);
        await Expect(login.SignedInText).ToHaveCountAsync(0);

        var navigations = RecordMainFrameNavigations();
        await login.ContinueWithAuthKit.ClickAsync();
        if (!Target.IsMock)
        {
            await SignInOnHostedPageAsync();
        }

        await App.ExpectAtAsync(Routes.Annotations);
        await App.WaitForReadyAsync();
        await Expect(App.UserMenu).ToHaveAccessibleNameAsync($"User menu for {name}");
        await ExpectSignedInThroughCallbackAsync(navigations);
    }

    // ACC-SGN-03: A signed-out deep link signs in and returns to the requested page.
    // Signed out, opening the target view's Configurations starts the sign-in with that page as its return path; the
    // sign-in passes /callback (on a deployed target after the AuthKit hosted page) and ends on Configurations.
    [Test]
    [TestCase(13)]
    public async Task SignIn_SignedOutDeepLink_ReturnsToThePage()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        var name = await App.SignedInNameAsync();
        await App.SignOutAsync();
        await Expect(new LoginPage(App).ContinueWithAuthKit).ToBeVisibleAsync();

        var navigations = RecordMainFrameNavigations();
        await App.GotoAsync(Routes.Configurations);
        if (!Target.IsMock)
        {
            await SignInOnHostedPageAsync();
        }

        await App.ExpectAtAsync(Routes.Configurations);
        await Expect(new ConfigurationsPage(App, Routes).Filter).ToBeVisibleAsync();
        await Expect(App.UserMenu).ToHaveAccessibleNameAsync($"User menu for {name}");
        await ExpectSignedInThroughCallbackAsync(navigations);
    }

    // ACC-SGN-04: The login, callback and unsupported pages handle the return path, sign-in errors and the configured
    // state.
    // Signed in, /login names the user and continues to a same-origin ?returnTo, but to '/' for a protocol-relative one;
    // an error of the query shows "Sign-in failed", also one that /callback passes on; /unsupported says the app is
    // configured and opens it.
    [Test]
    [TestCase(19)]
    public async Task SignIn_LoginPage_HandlesReturnPathAndErrors()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await Expect(organizations.Organization(Routes.Organization)).ToBeVisibleAsync();
        var name = await App.SignedInNameAsync();

        var login = new LoginPage(App);
        await login.OpenAsync(Routes.Login(returnTo: $"/{Routes.Organization}"));
        await Expect(login.SignedInText).ToHaveTextAsync($"You are signed in as {name}.");
        await Expect(login.Continue).ToHaveAttributeAsync("href", $"/{Routes.Organization}");
        await Expect(login.ContinueWithAuthKit).ToHaveCountAsync(0);

        await login.Continue.ClickAsync();
        await App.ExpectAtAsync(Routes.OrganizationOverview);
        await Expect(new OrganizationOverview(App, Routes).Heading).ToBeVisibleAsync();

        await login.OpenAsync(Routes.Login(returnTo: "//evil.example/x"));
        await Expect(login.Continue).ToHaveAttributeAsync("href", "/");

        await login.OpenAsync(Routes.Login(errorDescription: "Access denied by test"));
        await Expect(login.SignInErrorTitle).ToBeVisibleAsync();
        await Expect(login.SignInError).ToHaveTextAsync("Access denied by test");

        await App.GotoAsync(Routes.Callback(error: "access_denied"));
        await App.ExpectAtAsync(Routes.Login());
        await App.ExpectQueryAsync("error", "access_denied");
        await Expect(login.SignInErrorTitle).ToBeVisibleAsync();
        await Expect(login.SignInError).ToHaveTextAsync("access_denied");

        var unsupported = new UnsupportedPage(App, Routes);
        await unsupported.OpenAsync();
        await Expect(unsupported.Heading).ToHaveTextAsync(UnsupportedPage.ConfiguredTitle);
        await Expect(unsupported.Description).ToBeVisibleAsync();

        await unsupported.OpenTheAppLink.ClickAsync();
        await App.WaitForReadyAsync();
        await Expect(App.UserMenu).ToHaveAccessibleNameAsync($"User menu for {name}");
    }

    // ACC-SGN-05: The Account page shows the profile and the memberships, and a membership opens its project.
    // The user menu's Account shows the signed-in user's name and e-mail and the account id, which "Copy to clipboard"
    // copies; the target project's membership names its kind and the user's role and opens the project.
    [Test]
    [TestCase(20)]
    public async Task Account_Memberships_OpenTheProject()
    {
        await InstallClipboardRecorderAsync();
        await new OrganizationsPage(App, Routes).OpenAsync();
        var name = await App.SignedInNameAsync();

        await App.OpenUserMenuAsync();
        await App.UserMenuItem("Account").ClickAsync();
        await App.ExpectAtAsync(Routes.Account);
        var account = new AccountPage(App, Routes);
        await Expect(account.Heading).ToBeVisibleAsync();

        await Expect(account.ProfileName).ToHaveTextAsync(name);

        // The mock's pool e-mails are placeholders; the mock user is ada@example.com.
        await Expect(account.ProfileEmail).ToHaveTextAsync(Target.IsMock ? EmailAddress() : new Regex($"^{Regex.Escape(User.Email)}$", RegexOptions.IgnoreCase));
        await Expect(account.AccountId).ToHaveTextAsync(NotBlank());
        await Expect(account.SignOutButton).ToBeVisibleAsync();

        var accountId = await account.AccountIdAsync();
        await account.CopyAccountIdButton.ClickAsync();
        await WaitForCopiedTextAsync(accountId);

        var key = $"{Routes.Organization}.{Routes.Project}";
        var membership = account.Membership(key);
        await Expect(membership).ToHaveAccessibleNameAsync(new Regex($@"^{Regex.Escape(key)} Project (Reader|Contributor|Contributor \+ secrets|Administrator|Owner)$"));

        await membership.ClickAsync();
        await App.ExpectAtAsync((Routes with { View = AppRoutes.DefaultView }).Annotations);
    }

    // ACC-SGN-06: A new user creates the first organization and project on onboarding.
    // In the mock's new-user scenario Ada Lovelace has no account, so the app opens onboarding; its form refuses empty
    // and reserved names, then creates the organization and the project, whose card shows the trimmed description.
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
        await Expect(onboarding.Intro).ToBeVisibleAsync();
        await Expect(onboarding.CreateAndContinueButton).ToBeVisibleAsync();

        await onboarding.CreateAndContinueButton.ClickAsync();
        await Expect(onboarding.FieldError("Enter a organization name.")).ToBeVisibleAsync();
        await Expect(onboarding.FieldError("Enter a project name.")).ToBeVisibleAsync();

        await onboarding.OrganizationField.FillAsync("invitations");
        await onboarding.CreateAndContinueButton.ClickAsync();
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

    // The sign-in passed /callback, where AuthKit (and the mock's sign-in) returns to the app.
    private static async Task ExpectSignedInThroughCallbackAsync(IReadOnlyCollection<string> navigations)
    {
        await Assert.That(SignedInThroughCallback(navigations))
            .IsTrue()
            .Because($"the sign-in returns through /callback; the page went to {string.Join(", ", navigations)}");
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\S")]
    private static partial Regex NotBlank();
}
