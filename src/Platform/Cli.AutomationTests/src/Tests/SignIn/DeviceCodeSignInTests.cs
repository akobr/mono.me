using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.SignIn;

// Device Code: a pool user signs in with sform account and the AuthKit device page, in an empty SFORM_HOME.
[Area(Area.SignIn)]
public sealed class DeviceCodeSignInTests : CliTest
{
    // A sign-in that is not reused would start a device code and wait for it; a shorter timeout ends that sooner.
    private static readonly TimeSpan NoPromptTimeout = TimeSpan.FromSeconds(60);

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

    // SGN-DEV-02: The aliases access and login report the signed-in user like account.
    // In the leased user's signed-in home (no default project: every test starts without one), sform access and then
    // sform login each say "You are already logged in as <email>" without a device code, show the "Account ID: #<id>" that
    // sform account shows, and end with 43 as account does without a default project.
    [Test]
    [TestCase(236)]
    [UserRole(Role.Any)]
    [Covers("access")]
    [Covers("login")]
    public async Task Account_Aliases_ReportSignedInUser()
    {
        var accountId = await AccountIdAsync();

        foreach (var alias in new[] { "access", "login" })
        {
            var result = await Sform(
                new SformRunOptions { Environment = { [SformProcessEnvironment.DeviceCodeOutput] = "json" }, Timeout = NoPromptTimeout },
                alias);

            result.ShouldExitWith(CliExit.InteractionNeeded)
                .ShouldContain($"You are already logged in as {User.Email}", comparison: StringComparison.OrdinalIgnoreCase)
                .ShouldContain($"Account ID: #{accountId}")
                .ShouldContain("No default project is set, please call sform account set command.")
                .ShouldNotContain("To sign in, open");
            await Assert.That(result.ErrorLines.Any(DeviceCodePrompt.IsPrompt)).IsFalse();
        }
    }

    // SGN-DEV-03: Without SFORM_DEVICE_CODE_OUTPUT the prompt is text with the URL, the code, the complete URL and the expiry.
    // In an empty home, sform account [-o <AuthKit organization id>] without SFORM_DEVICE_CODE_OUTPUT prints the prompt on
    // stdout only: "To sign in, open <verification URI> and enter the code <code>.", "Or open <complete URI> with the code
    // filled in." and "The code expires in N minute(s)."; the code read from that text is approved, sform signs the user in
    // (43 without a default project), and stderr carries no JSON line.
    [Test]
    [TestCase(237)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account")]
    public async Task DeviceCode_TextPrompt_ShowsCodeAndExpiry()
    {
        var signIn = await SignInAsync(options: new DeviceCodeOptions { JsonPrompt = false });

        var prompt = signIn.Prompt ?? throw signIn.Result.Fail("printed no text prompt with a code");
        var complete = prompt.VerificationUriComplete ?? throw signIn.Result.Fail("printed no complete URI (\"Or open <complete URI> with the code filled in.\")");
        signIn.Result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain($"To sign in, open {prompt.VerificationUri.AbsoluteUri} and enter the code {prompt.UserCode}.")
            .ShouldContain($"Or open {complete.AbsoluteUri} with the code filled in.")
            .ShouldMatch(new Regex(@"The code expires in \d+ minute\(s\)\."))
            .ShouldContain($"You have been logged in as {User.Email}", comparison: StringComparison.OrdinalIgnoreCase);
        await Assert.That(signIn.Result.ErrorLines.Any(DeviceCodePrompt.IsPrompt)).IsFalse();
        await Assert.That(Home.HasSession).IsTrue();
    }

    // SGN-DEV-04: -b opens the complete URL; without an opener the CLI says to open it by hand.
    // (a) In an empty home with SFORM_BROWSER set to a recording helper (the fake editor's apphost, which logs its arguments
    // and does nothing else), sform account [-o …] -b starts it with verificationUriComplete as its only argument and the
    // sign-in succeeds (43). (b) In another empty home with SFORM_BROWSER naming a program that does not exist, sform account
    // [-o …] --browser says "The browser could not be opened, please open the address manually." and still signs in.
    [Test]
    [TestCase(238)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account", "-b", "--browser")]
    public async Task DeviceCode_Browser_OpensSignInPage()
    {
        var browser = new FakeEditor(WorkingDirectory, FileSystem);
        var recording = browser.With(EditorChange.None);
        var environment = new Dictionary<string, string?>(recording.Environment, StringComparer.Ordinal)
        {
            [SformProcessEnvironment.Browser] = browser.Command,
        };

        var opened = await SignInAsync(options: new DeviceCodeOptions { Arguments = ["-b"], Environment = environment });

        var prompt = opened.Prompt ?? throw opened.Result.Fail("printed no device code");
        opened.Result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldNotContain("The browser could not be opened");
        var launch = browser.ReadLog(recording).Single();
        await Assert.That(launch.Arguments).IsEquivalentTo(new[] { prompt.Address.AbsoluteUri });

        var missing = FileSystem.Path.Combine(WorkingDirectory, $"no-browser-{CliData.Data.Hex(8)}");
        var notOpened = await SignInAsync(
            FreshHome(),
            new DeviceCodeOptions { Arguments = ["--browser"], Environment = { [SformProcessEnvironment.Browser] = missing } });

        notOpened.Result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("The browser could not be opened, please open the address manually.")
            .ShouldContain($"You have been logged in as {User.Email}", comparison: StringComparison.OrdinalIgnoreCase);
    }

    // SGN-DEV-05: A sign-in declined on the device page ends with exit 44 and stores no session.
    // In an empty home, sform account [-o …] is declined on the device page: "The sign-in was declined." and 44, and the
    // home holds no session; a following sform account in that home starts a new device code (another code on stderr),
    // which the test does not approve (it stops the run).
    [Test]
    [TestCase(239)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account")]
    public async Task DeviceCode_Declined_StoresNoSession()
    {
        var declined = await SignInAsync(options: new DeviceCodeOptions { Decline = true });

        var prompt = declined.Prompt ?? throw declined.Result.Fail("printed no device code");
        declined.Result.ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("The sign-in was declined.");
        await Assert.That(Home.HasSession).IsFalse();

        await using var again = await StartSform(
            new SformRunOptions { Environment = { [SformProcessEnvironment.DeviceCodeOutput] = "json" }, Label = "after the declined sign-in" },
            AccountArguments());
        again.CloseInput();
        var line = await again.WaitForLineOrExitAsync(SformStream.StdErr, DeviceCodePrompt.IsPrompt, NoPromptTimeout)
            ?? throw (await again.WaitForExitAsync()).Fail("started no new device code after the declined sign-in");
        await again.KillAsync();

        await Assert.That(DeviceCodePrompt.Parse(line).UserCode).IsNotEqualTo(prompt.UserCode);
        await Assert.That(Home.HasSession).IsFalse();
    }

    // SGN-DEV-06: A user in several organizations without -o gets exit 2 and the list of organizations on redirected input,
    // and -o picks one.
    // The pool user the target marks as a member of two organizations, in an empty home: sform account without -o (input
    // redirected), approved, ends with 2 within the timeout and stores no session; stderr says "! sform cannot ask for the
    // organization to sign in to: the input is redirected." and "You belong to several organizations; pass one with
    // -o|--organization <id>:" and lists every organization with its id and name (two at least). sform account -o <id of
    // the second organization>, approved, signs in to it and ends with 43.
    [Test]
    [TestCase(240)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [RequiresFeature(CliFeatures.MultiOrganization)]
    [Covers("account", "-o")]
    public async Task DeviceCode_SeveralOrganizations_RequiresOrganization()
    {
        var redirected = await SignInAsync(options: new DeviceCodeOptions { Organization = string.Empty, ExpectSignedIn = false });

        redirected.Result.ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("! sform cannot ask for the organization to sign in to: the input is redirected.", SformStream.StdErr)
            .ShouldContain("You belong to several organizations; pass one with -o|--organization <id>:", SformStream.StdErr);
        var organizations = SignInOutput.OrganizationsOf(redirected.Result);
        if (organizations.Count < 2)
        {
            throw redirected.Result.Fail($"listed {organizations.Count} organization(s) as \"<id>  <name>\" lines, expected two at least");
        }

        await Assert.That(Home.HasSession).IsFalse();

        var second = organizations[1].Id;
        var signIn = await SignInAsync(options: new DeviceCodeOptions { Organization = second });

        signIn.Result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain($"You have been logged in as {User.Email}", comparison: StringComparison.OrdinalIgnoreCase);
        await Assert.That(Home.HasSession).IsTrue();
    }

    // SGN-DEV-07: -o with an organization the user is not a member of fails with exit 2, on a sign-in and against a stored
    // session.
    // In an empty home, sform account -o <generated id> and sform account --organization <generated id>, each approved, end
    // with 2 and "You are not a member of the organization '<generated id>'" followed by the user's organizations (or the
    // only one WorkOS signed the user in to, or none), and store no session. After a sign-in as in SGN-DEV-01, sform account
    // --organization <generated id> against the stored session ends with 2 before any call: "You are signed in as <email>
    // to the organization '<id>', and -o|--organization asks for '<generated id>'." (or "… without an organization, …") and
    // "Sign out with sform account logout, then sign in with -o."
    [Test]
    [TestCase(241)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account", "-o", "--organization")]
    public async Task DeviceCode_UnknownOrganization_Fails()
    {
        var generated = $"org_01{CliData.Data.Hex(24).ToUpperInvariant()}";
        var notMember = new Regex(
            $@"You are not a member of the organization '{Regex.Escape(generated)}'(\. Your organizations: .+\.|: WorkOS signed you in to your only organization, '[^']+'\.|: WorkOS signed you in without an organization\.)",
            RegexOptions.CultureInvariant);

        foreach (var option in new[] { "-o", "--organization" })
        {
            var refused = await SignInAsync(options: new DeviceCodeOptions { Organization = generated, OrganizationOption = option, ExpectSignedIn = false });

            refused.Result.ShouldExitWith(CliExit.WrongInput).ShouldMatch(notMember);
            await Assert.That(refused.Prompt).IsNotNull();
            await Assert.That(Home.HasSession).IsFalse();
        }

        (await SignInAsync()).Result.ShouldExitWith(CliExit.InteractionNeeded);

        var stored = await Sform(new SformRunOptions { Timeout = NoPromptTimeout }, "account", "--organization", generated);

        stored.ShouldExitWith(CliExit.WrongInput)
            .ShouldMatch(new Regex(
                $@"You are signed in as {Regex.Escape(User.Email)} (to the organization '[^']+'|without an organization), and -o\|--organization asks for '{Regex.Escape(generated)}'\.",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ShouldContain("Sign out with sform account logout, then sign in with -o.")
            .ShouldNotContain("To sign in, open");
        await Assert.That(Home.HasSession).IsTrue();
    }

    // sform account with -o <Target:AuthKitOrganizationId> when it is set, as the sign-in passes it.
    private string[] AccountArguments()
    {
        return Target.AuthKitOrganizationId is { } organization ? ["account", "-o", organization] : ["account"];
    }
}
