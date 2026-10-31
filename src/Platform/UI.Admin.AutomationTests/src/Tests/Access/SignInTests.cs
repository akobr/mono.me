using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

[Area(Area.Access)]
public sealed class SignInTests : PlatformTest
{
    // The context starts signed in (the cached sign-in of the pool user, or the mock's user): /orgs lists the default
    // organization with its default project.
    [Test]
    [Elementary]
    public async Task SignIn_ShowsOrganizations()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();

        await Expect(organizations.Heading).ToBeVisibleAsync();
        await Expect(App.UserMenu).ToBeVisibleAsync();
        await Expect(organizations.Organization(Target.Organization)).ToBeVisibleAsync();
        await Expect(organizations.Project(Target.Organization, Target.Project)).ToBeVisibleAsync();
    }
}
