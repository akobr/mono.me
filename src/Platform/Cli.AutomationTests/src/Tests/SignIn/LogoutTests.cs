using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests.Tests.SignIn;

// Logout and Registration: sform account logout and sform account register, in homes of the test's own.
[Area(Area.SignIn)]
public sealed class LogoutTests : CliTest
{
    // SGN-OUT-04 leases the unregistered pool user (SignInOutput.FeatureUser), every other case any pool user.
    protected override UserRequirement RequiredUser =>
        TestContext.Current?.Metadata.TestDetails.MethodName == nameof(Register_NewUser_CreatesAccountAndProject)
            ? SignInOutput.FeatureUser(CliFeatures.Registration)
            : base.RequiredUser;

    // SGN-OUT-01: account logout removes the session, and the next command is refused.
    // After a sign-in as in SGN-DEV-01 in an empty home, sform account logout says "You have been logged out from account:
    // <name>" (the name of the account header) and ends with 0, and the home's token store is empty; sform story -p
    // <org>.<project> then calls the API without a token and ends with 44 and "You are not authorized to execute the command
    // or you don't have access to requested resources."
    [Test]
    [TestCase(246)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account logout")]
    public async Task Logout_SignedIn_RemovesSession()
    {
        var signIn = await SignInAsync();
        var name = SignInOutput.AccountNameOf(signIn.Result, User.Email);
        await Assert.That(Home.HasSession).IsTrue();

        (await Sform("account", "logout"))
            .ShouldSucceed()
            .ShouldContain($"You have been logged out from account: {name}");
        await Assert.That(StoredSession()).IsNull();

        (await Sform(InProject("story")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain(GeneralRefusal);
    }

    // SGN-OUT-02: account logout without a session or with machine credentials changes nothing and exits 0.
    // In an empty home, sform account logout says "No account is logged in this CLI instance." and ends with 0. With the
    // variables of an API key machine (a made-up key of the target project: logout calls nothing for a machine) it says "sform
    // uses the machine credentials in STORYTELLER_API_KEY. Nothing is stored, so there is nothing to log out; unset the
    // variables to sign in as a user." and ends with 0, the key in no output.
    [Test]
    [TestCase(248)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account logout")]
    public async Task Logout_NoSessionOrMachine_NothingToDo()
    {
        (await Sform("account", "logout"))
            .ShouldSucceed()
            .ShouldContain("No account is logged in this CLI instance.");
        await Assert.That(Home.HasSession).IsFalse();

        var apiKey = SignInOutput.MadeUpApiKey(Target.ProjectKey, CliData.MachineName(), CliData.SecretValue());
        var machine = MachineCredentials.ForApiKey(apiKey, Target.BaseUrl ?? string.Empty, Target.ProjectKey, name: "a made-up API key machine");

        (await Sform(AsMachine(machine), "account", "logout"))
            .ShouldSucceed()
            .ShouldContain($"sform uses the machine credentials in {MachineCredentials.ApiKeyVariable}. Nothing is stored, so there is nothing to log out; unset the variables to sign in as a user.")
            .ShouldNotContain(apiKey)
            .ShouldNotContain(apiKey, SformStream.StdErr);
    }

    // SGN-OUT-03: account register for a registered user says so and exits 0; without a sign-in the API refuses it with
    // exit 44.
    // After a sign-in as in SGN-DEV-01, sform account register says "You are already registered as <email> #<id>" (the id of
    // the sign-in's summary) and ends with 0 without a prompt; in another, empty home sform account register calls the API
    // without a token and ends with 44 and "You are not authorized to execute the command or you don't have access to
    // requested resources."
    [Test]
    [TestCase(249)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account register")]
    public async Task Register_RegisteredOrSignedOut_Reports()
    {
        var signIn = await SignInAsync();
        var accountId = AccountOutput.AccountIdOf(signIn.Result);

        (await Sform("account", "register"))
            .ShouldSucceed()
            .ShouldContain($"You are already registered as {User.Email} #{accountId}", comparison: StringComparison.OrdinalIgnoreCase)
            .ShouldNotContain("cannot ask for", SformStream.StdErr);

        var signedOut = FreshHome();
        (await Sform(new SformRunOptions { Home = signedOut, Label = "in an empty home" }, "account", "register"))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain(GeneralRefusal);
        await Assert.That(signedOut.HasSession).IsFalse();
    }

    // SGN-OUT-04: account register creates the account, the first project and the default project of a new user; on
    // redirected input it stops at the prompt.
    // The unregistered pool user the target marks for registration signs in (sform account: "You account is not registered",
    // 43). sform account register on redirected input ends with 43 at the prompt and creates nothing (no default project);
    // stderr says "! sform cannot ask for the organization and the project of the new account: the input is redirected." and
    // "Run sform account register in a terminal." In a terminal, sform account register asks for the organization and the
    // project, answered with generated names: "Account <email> (<name>) has been created." and "Default project set to
    // <org>.<project> with default view.", and access.default.json names them.
    [Test]
    [TestCase(250)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Terminal]
    [RequiresFeature(CliFeatures.Registration)]
    [Covers("account register")]
    public async Task Register_NewUser_CreatesAccountAndProject()
    {
        var signIn = await SignInAsync();
        signIn.Result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("You account is not registered, to create a registration call sform account register command.");

        (await Sform("account", "register"))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the organization and the project of the new account: the input is redirected.", SformStream.StdErr)
            .ShouldContain("Run sform account register in a terminal.", SformStream.StdErr)
            .ShouldNotContain("has been created");
        await Assert.That(Home.HasDefaultProject).IsFalse();

        var organization = CliData.Slug("org");
        var project = CliData.ProjectName();
        await using var terminal = await StartSformInTerminal("account", "register");
        await terminal.ExpectAsync("Name of your organization");
        await terminal.SendLineAsync(organization);
        await terminal.ExpectAsync("Name of your project");
        await terminal.SendLineAsync(project);
        await terminal.ExpectAsync(new Regex($@"Account\s*{Regex.Escape(User.Email)}\s*\(.+\)\s*has\s*been\s*created\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        await terminal.ExpectAsync($"Default project set to {organization}.{project} with default view.");
        (await terminal.WaitForExitAsync()).ShouldSucceed();

        var access = JsonNode.Parse(Home.ReadText(SformHome.AccessDefaultFileName) ?? "null")?["access"];
        await Assert.That(access?["OrganizationName"]?.GetValue<string>()).IsEqualTo(organization);
        await Assert.That(access?["ProjectName"]?.GetValue<string>()).IsEqualTo(project);
        await Assert.That(access?["ViewName"]?.GetValue<string>()).IsEqualTo("default");
    }

    // The stored AuthKit session of the home: its text when the token store holds one, null when the store is empty (the
    // file removed, or left without content).
    private string? StoredSession()
    {
        var text = Home.ReadText(SformHome.AuthKitSessionFileName);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
