using _42.Platform.Automation.Shared.Access;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Automation.Shared.UnitTests.Access;

// What the AuthKit hosted page's steps decide without a browser: the host check, the password of a pool user, the name
// of the e-mail step's button.
public sealed class AuthKitHostedPageTests
{
    private static readonly Uri Site = new("https://admin.example.test/app/");

    [Theory]
    [InlineData("https://admin.example.test/callback?code=1")]
    [InlineData("https://ADMIN.example.test/")]
    [InlineData("https://admin.example.test:443/orgs")]
    public void IsOnHost_IsTrue_OnTheSchemeHostAndPortOfTheSite(string url)
    {
        AuthKitHostedPage.IsOnHost(url, Site).ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://example.authkit.app/sign-in")]
    [InlineData("http://admin.example.test/")]
    [InlineData("https://admin.example.test:8443/")]
    [InlineData("/callback")]
    [InlineData("about:blank")]
    [InlineData("")]
    public void IsOnHost_IsFalse_ElsewhereAndForARelativeUrl(string url)
    {
        AuthKitHostedPage.IsOnHost(url, Site).ShouldBeFalse();
    }

    [Fact]
    public void PasswordOf_IsThePoolUsersPassword_AndNamesTheSecretWhenItIsMissing()
    {
        AuthKitHostedPage.PasswordOf(new TestUser(0, "ada@example.test", "pw-1")).ShouldBe("pw-1");

        var missing = new TestUser(3, "grace@example.test");
        var exception = Should.Throw<InvalidOperationException>(() => AuthKitHostedPage.PasswordOf(missing));

        exception.Message.ShouldBe($"{missing} has no password; set Users:Pool:3:Password in the user secrets.");
        exception.Message.ShouldNotContain("grace@example.test");
    }

    [Theory]
    [InlineData("Continue", true)]
    [InlineData("Continue with email", true)]
    [InlineData("Continue with email Last used", true)]
    [InlineData("ContinueLast used", true)]
    [InlineData("Continue with Google", false)]
    [InlineData("Sign in", false)]
    public void ContinueButton_MatchesTheEmailStepsButton_OnlyThatOne(string name, bool matches)
    {
        AuthKitHostedPage.ContinueButton.IsMatch(name).ShouldBe(matches);
    }
}
