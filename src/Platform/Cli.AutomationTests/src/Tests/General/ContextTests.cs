using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.General;

// Context Options: which project and view a context command targets: -p/-v, the default project of sform.settings.json,
// house.main, and a machine's STORYTELLER_PROJECT and STORYTELLER_VIEW. A view exists by its data, so each case puts an
// annotation (as an Editor) into a generated view and recognises the target by it.
[Area(Area.General)]
public sealed class ContextTests : CliTest
{
    // GEN-CTX-01: -p and -v win over the default project and view.
    // <A> is the default project with the view default (sform account set -p <A> -v default stores any well-formed key,
    // ACC-SET-04, so <A> is a generated project of the organization, which no pool user may read); <B> is the target
    // project, where an Editor put <rst> into a generated view <v2>. sform story -p <B> lists B's default view (0: not the
    // refusal of A); sform story --projectKey <B> --view <v2> lists <rst>, the annotation of B's view v2; sform story -v <v2>
    // targets A's view v2: the API refuses A with 44 and "No Reader access to '<A>'.", so -v alone kept the default project.
    [Test]
    [TestCase(374)]
    [UserRole(Role.Any)]
    [Covers("story", "-p", "--projectKey", "-v", "--view")]
    public async Task Context_ExplicitWinsOverDefault()
    {
        var a = $"{Target.Organization}.{CliData.ProjectName()}";
        var b = ProjectKey;
        var v2 = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var editor = await ArrangeAsEditorAsync();
        (await editor.Sform("story", "set", key, "-p", b, "-v", v2)).ShouldSucceed();
        (await Sform("account", "set", "-p", a, "-v", "default")).ShouldSucceed();

        (await Sform("story", "-p", b))
            .ShouldSucceed()
            .ShouldHaveLine("# Annotations")
            .ShouldNotContain($"'{a}'");

        (await Sform("story", "--projectKey", b, "--view", v2))
            .ShouldSucceed()
            .ShouldHaveLine($"- {key}");

        (await Sform("story", "-v", v2))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Reader access to '{a}'.")
            .ShouldNotContain(key);
    }

    // GEN-CTX-02: Without a default project and without -p a context command targets house.main with the view default.
    // With no default project, sform story sends its request to house.main with the view default, which the leased user
    // is no member of: the API refuses it with 44, "No Reader access to 'house.main'." and "Error code: AccessDenied", and
    // sform story does not point to sform account set (only sform account does).
    [Test]
    [TestCase(376)]
    [UserRole(Role.Any)]
    [Covers("story")]
    public async Task Context_NoProject_TargetsHouseMain()
    {
        await Assert.That(Home.HasDefaultProject).IsFalse();

        (await Sform("story"))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("No Reader access to 'house.main'.")
            .ShouldContain("Error code: AccessDenied")
            .ShouldNotContain("sform account set");
    }

    // GEN-CTX-03: A -p without a dot is ignored, and a view without data is empty.
    // The default is the target project <A> with a generated view where an Editor put <rst>. sform story -p only-org ignores
    // the -p and lists A's annotations of that view (<rst>) with 0; sform story -p <A> -v <generated> prints "No annotation
    // exists yet." with 0: a view exists by its data.
    [Test]
    [TestCase(378)]
    [UserRole(Role.Any)]
    [Covers("story", "-p", "-v")]
    public async Task Context_ProjectWithoutDotOrEmptyView()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var editor = await ArrangeAsEditorAsync();
        (await editor.Sform("story", "set", key, "-p", ProjectKey, "-v", view)).ShouldSucceed();
        (await Sform("account", "set", "-p", ProjectKey, "-v", view)).ShouldSucceed();

        (await Sform("story", "-p", "only-org"))
            .ShouldSucceed()
            .ShouldHaveLine($"- {key}");

        (await Sform("story", "-p", ProjectKey, "-v", CliData.ViewName()))
            .ShouldSucceed()
            .ShouldContain("No annotation exists yet.")
            .ShouldNotContain(key);
    }

    // GEN-CTX-04: STORYTELLER_PROJECT and STORYTELLER_VIEW target a machine, and -p/-v win over them.
    // An Editor put <rst> into a generated view of the machine's project. As the configured API key machine with
    // STORYTELLER_PROJECT=<org>.<project> and STORYTELLER_VIEW=<that view>: sform story lists <rst> (the variables' view);
    // sform story -v default lists the default view, without <rst>; sform story -p <org>.<other> ends with 2 before any call:
    // "The machine credentials in STORYTELLER_API_KEY belong to <org>.<project>, not <org>.<other>."
    [Test]
    [TestCase(379)]
    [MachineOnly]
    [RequiresMachine(MachineCatalog.ApiKeyMachine)]
    [Covers("story", "-p", "-v")]
    public async Task Context_MachineVariables_Target()
    {
        var configured = MachineOf(MachineCatalog.ApiKeyMachine);
        var apiKey = configured.Variables[MachineCredentials.ApiKeyVariable]!;
        var project = Machines[MachineCatalog.ApiKeyMachine].Project ?? MachineCredentials.ProjectKeyOf(apiKey) ?? Target.ProjectKey;
        var organization = project[..project.IndexOf('.', StringComparison.Ordinal)];
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var editor = await ArrangeAsEditorAsync();
        (await editor.Sform("story", "set", key, "-p", project, "-v", view)).ShouldSucceed();

        var machine = AsMachine(configured.With(new Dictionary<string, string?>
        {
            [MachineCredentials.Project] = project,
            [MachineCredentials.View] = view,
            [MachineCredentials.Key] = null,
        }));

        (await Sform(machine, "story"))
            .ShouldSucceed()
            .ShouldHaveLine($"- {key}");

        (await Sform(machine, "story", "-v", "default"))
            .ShouldSucceed()
            .ShouldHaveLine("# Annotations")
            .ShouldNotContain(key);

        var other = $"{organization}.{CliData.ProjectName()}";
        (await Sform(machine, "story", "-p", other))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The machine credentials in {MachineCredentials.ApiKeyVariable} belong to {project}, not {other}.", comparison: StringComparison.OrdinalIgnoreCase);
    }
}
