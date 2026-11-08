using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// Default Project: sform account set stores the default project and view in the home's access.default.json.
[Area(Area.Account)]
public sealed class AccountSetTests : CliTest
{
    // ACC-SET-01: account set -p -v stores the default project and view, and later commands use them.
    // account set -p <org>.<project> -v <view> (a generated view: a view exists by its data, and with "default" a set
    // that ignored -v would pass) confirms the default and writes access.default.json; account then ends with 0 and names
    // it ("Default project set to <org>.<project> with <view> view."); story without -p and -v reads that project's view
    // instead of asking for account set (43). account set --projectKey … --viewName default names the current default
    // ("Currently the default project is set to … and view is <view>.") and switches the view to default, as account and
    // the file show.
    [Test]
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

    // access.default.json of the home: { "access": { "OrganizationName", "ProjectName", "ViewName" } }.
    private async Task AssertDefaultAsync(string view)
    {
        var text = Home.ReadText(Access.SformHome.AccessDefaultFileName);
        await Assert.That(text).IsNotNull();

        var access = JsonNode.Parse(text!)!["access"]!;
        await Assert.That(access["OrganizationName"]!.GetValue<string>()).IsEqualTo(Organization);
        await Assert.That(access["ProjectName"]!.GetValue<string>()).IsEqualTo(Project);
        await Assert.That(access["ViewName"]!.GetValue<string>()).IsEqualTo(view);
    }
}
