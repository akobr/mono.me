using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.SignIn;

// Device Code: a pool user signs in with sform account and the AuthKit device page, in an empty SFORM_HOME.
[Area(Area.SignIn)]
public sealed class DeviceCodeSignInTests : CliTest
{
    // SGN-DEV-01: A pool user signs in with the device code and sees the account summary.
    // In an empty home, sform account [-o <AuthKit organization id>] writes the code as JSON, the first line on stderr;
    // the device page approves verificationUriComplete as the leased user; sform says who is logged in, shows the header
    // "<name> @ <email>", the account id and the access points, says that no default project is set, and ends with 43.
    // A second sform account (with the same -o) reuses the session: "You are already logged in as <email>" and no code.
    [Test]
    [TestCase(234)]
    [Elementary]
    [FreshSignIn]
    [UserRole(Role.Any)]
    [Covers("account")]
    public async Task DeviceCode_SignIn_ShowsAccount()
    {
        var signIn = await SignInAsync();

        await Assert.That(signIn.Prompt).IsNotNull();
        var email = Regex.Escape(User.Email);
        signIn.Result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain($"You have been logged in as {User.Email}", comparison: StringComparison.OrdinalIgnoreCase)
            .ShouldMatch(new Regex($@"(?m)^# .+ @ {email}\s*$", RegexOptions.IgnoreCase))
            .ShouldMatch(new Regex(@"Account ID: #\S+"))
            .ShouldMatch(new Regex(@"You have access to \d+ access points\."))
            .ShouldContain("No default project is set, please call sform account set command.");
        await Assert.That(Home.HasSession).IsTrue();

        var organization = Target.AuthKitOrganizationId;
        string[] again = organization is null ? ["account"] : ["account", "-o", organization];

        // A session that is not reused would start a device code and wait for it; a shorter timeout ends that sooner.
        var second = await Sform(
            new SformRunOptions { Environment = { [SformProcessEnvironment.DeviceCodeOutput] = "json" }, Timeout = TimeSpan.FromSeconds(60) },
            again);

        second.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain($"You are already logged in as {User.Email}", comparison: StringComparison.OrdinalIgnoreCase)
            .ShouldNotContain("To sign in, open");
        await Assert.That(second.ErrorLines.Any(DeviceCodePrompt.IsPrompt)).IsFalse();
    }
}
