using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// Default Project: sform account set stores the default project and view in the access section of the home's
// sform.settings.json.
[Area(Area.Account)]
public sealed class AccountSetTests : CliTest
{
    // ACC-SET-01: account set -p -v stores the default project and view, and later commands use them.
    // account set -p <org>.<project> -v <view> (a generated view: a view exists by its data, and with "default" a set
    // that ignored -v would pass) confirms the default and writes it to sform.settings.json; account then ends with 0 and names
    // it ("Default project set to <org>.<project> with <view> view."); story without -p and -v reads that project's view
    // instead of asking for account set (43). account set --projectKey … --viewName default names the current default
    // ("Currently the default project is set to … and view is <view>.") and switches the view to default, as account and
    // the file show.
    [Test]
    [TestCase(268)]
    [Elementary]
    [UserRole(Role.Any)]
    [Covers("account set", "-p", "--projectKey", "-v", "--viewName")]
    public async Task AccountSet_ProjectAndView_BecomeDefault()
    {
        await Assert.That(Home.HasDefaultProject).IsFalse();
        var view = CliData.ViewName();

        (await Sform("account", "set", "-p", ProjectKey, "-v", view))
            .ShouldSucceed()
            .ShouldContain($"Default project set to {ProjectKey} with view {view}.");
        await AssertDefaultAsync(view);

        (await Sform("account"))
            .ShouldSucceed()
            .ShouldContain($"Default project set to {ProjectKey} with {view} view.");

        (await Sform("story"))
            .ShouldSucceed()
            .ShouldNotContain("sform account set")
            .ShouldNotContain("No default project");

        (await Sform("account", "set", "--projectKey", ProjectKey, "--viewName", "default"))
            .ShouldSucceed()
            .ShouldContain($"Currently the default project is set to {ProjectKey} and view is {view}.")
            .ShouldContain($"Default project set to {ProjectKey} with view default.");
        await AssertDefaultAsync("default");

        (await Sform("account"))
            .ShouldSucceed()
            .ShouldContain($"Default project set to {ProjectKey} with default view.");
    }

    // ACC-SET-02: account set in a terminal offers the user's projects and stores the choice.
    // With the default set to the target project (account set -p <org>.<project>), sform account set in a terminal names the
    // current default and asks "Which project would you like to set as default" with the user's projects, the target project
    // among them and preselected: Enter alone picks it, sform says "Default project set to <org>.<project> with view
    // default." and sform.settings.json names it.
    [Test]
    [TestCase(269)]
    [UserRole(Role.Any)]
    [Terminal]
    [Covers("account set")]
    public async Task AccountSet_Interactive_SelectsProject()
    {
        (await Sform("account", "set", "-p", ProjectKey)).ShouldSucceed();

        await using var terminal = await StartSformInTerminal("account", "set");
        await terminal.ExpectAsync($"Currently the default project is set to {ProjectKey} and view is default.");
        await terminal.ExpectAsync("Which project would you like to set as default");
        await terminal.ExpectAsync(ProjectKey);
        await terminal.SendKeyAsync(TerminalKey.Enter);
        await terminal.ExpectAsync($"Default project set to {ProjectKey} with view default.");
        (await terminal.WaitForExitAsync()).ShouldSucceed();

        await AssertDefaultAsync("default");
    }

    // ACC-SET-03: account set without -p on redirected input fails with exit 2.
    // sform account set with the input redirected ends with 2 within the timeout, before any call (the list it would offer
    // needs a terminal); stderr says "! sform cannot ask for the default project: the input is redirected." and "Pass it with
    // -p|--projectKey <organizationName>.<projectName>.", and no default project is written.
    [Test]
    [TestCase(270)]
    [UserRole(Role.Any)]
    [Covers("account set")]
    public async Task AccountSet_RedirectedWithoutProject_Fails()
    {
        (await Sform(new SformRunOptions { Timeout = TimeSpan.FromSeconds(30) }, "account", "set"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("! sform cannot ask for the default project: the input is redirected.", SformStream.StdErr)
            .ShouldContain("Pass it with -p|--projectKey <organizationName>.<projectName>.", SformStream.StdErr)
            .ShouldNotContain("Default project set to");
        await Assert.That(Home.HasDefaultProject).IsFalse();
    }

    // ACC-SET-04: account set -p checks only the format: a key without a dot fails with exit 2, an unknown project is stored.
    // With the default set to the target project, sform account set -p only-org ends with 2 before any call ("'only-org'
    // is no project key; it takes <organization>.<project>.") and leaves sform.settings.json as it was;
    // sform account set -p <org>.<generated> is not checked against the account: it stores the generated project as the
    // default and ends with 0.
    [Test]
    [TestCase(271)]
    [UserRole(Role.Any)]
    [Covers("account set", "-p")]
    public async Task AccountSet_ProjectKey_ChecksFormatOnly()
    {
        (await Sform("account", "set", "-p", ProjectKey)).ShouldSucceed();
        var before = Home.ReadText(Access.SformHome.SettingsFileName);

        (await Sform("account", "set", "-p", "only-org"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("'only-org' is no project key; it takes <organization>.<project>.");
        await Assert.That(Home.ReadText(Access.SformHome.SettingsFileName)).IsEqualTo(before);

        var generated = CliData.ProjectName();
        (await Sform("account", "set", "-p", $"{Organization}.{generated}"))
            .ShouldSucceed()
            .ShouldContain($"Default project set to {Organization}.{generated} with view default.");
        await AssertDefaultAsync("default", generated);
    }

    // The access section of the home's sform.settings.json: { "access": { "organizationName", "projectName", "viewName" } }.
    private async Task AssertDefaultAsync(string view, string? project = null)
    {
        await Assert.That(Home.ReadSettings()?["access"]).IsNotNull();
        await Assert.That(Home.AccessSetting("organizationName")).IsEqualTo(Organization);
        await Assert.That(Home.AccessSetting("projectName")).IsEqualTo(project ?? Project);
        await Assert.That(Home.AccessSetting("viewName")).IsEqualTo(view);
    }
}
