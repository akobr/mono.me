using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.SignIn;

// Machine Sign-in: a machine signs in from the STORYTELLER_* variables, without a prompt and without a user's state.
[Area(Area.SignIn)]
public sealed class MachineSignInTests : CliTest
{
    // SGN-MSI-01: A machine with an API key signs in from the STORYTELLER_* variables without a prompt.
    // As the configured API key machine (Machines:apikey: STORYTELLER_ENDPOINT and STORYTELLER_API_KEY, in an empty home),
    // sform account ends with 0: "Signed in as machine <id> with an API key (STORYTELLER_API_KEY).", "Project
    // <org>.<project> with <view> view." and "The API checks the credentials on the first call."; no device code, the
    // key in no output, and no session or default project in the home.
    [Test]
    [TestCase(243)]
    [Elementary]
    [MachineOnly]
    [RequiresMachine(MachineCatalog.ApiKeyMachine)]
    [Covers("account")]
    public async Task Machine_ApiKey_SignsIn()
    {
        var machine = MachineOf(MachineCatalog.ApiKeyMachine);
        var settings = Machines[MachineCatalog.ApiKeyMachine];
        var apiKey = machine.Variables[MachineCredentials.ApiKeyVariable]!;

        var result = await Sform(AsMachine(machine), "account");

        result.ShouldSucceed()
            .ShouldHaveLine("# Machine sign-in")
            .ShouldContain("with an API key (STORYTELLER_API_KEY).")
            .ShouldContain("The API checks the credentials on the first call.")
            .ShouldNotContain(apiKey)
            .ShouldNotContain(apiKey, SformStream.StdErr)
            .ShouldNotContain("To sign in, open");
        await Assert.That(result.ErrorLines.Any(DeviceCodePrompt.IsPrompt)).IsFalse();

        if (machine.MachineId is { } id)
        {
            result.ShouldContain($"Signed in as machine {id} with an API key (STORYTELLER_API_KEY).");
        }

        if ((machine.ProjectKey ?? MachineCredentials.ProjectKeyOf(apiKey)) is { } project)
        {
            var view = settings.View ?? (settings.Key is null ? "default" : null);
            result.ShouldContain(view is null ? $"Project {project} with " : $"Project {project} with {view} view.");
        }

        var home = new SformHome(result.Home, SformHomeKind.Machine, FileSystem);
        await Assert.That(home.HasSession).IsFalse();
        await Assert.That(home.HasDefaultProject).IsFalse();
    }

    // SGN-MSI-02: STORYTELLER_KEY gives the machine its project and view, and -p wins over it.
    // An Editor creates <rst> in a generated view of the machine's project (only there, so a read that finds it read that
    // view). As the configured API key machine with STORYTELLER_KEY=@<org>.<project>.<view>.<rst> instead of
    // STORYTELLER_PROJECT and STORYTELLER_VIEW, sform story get <rst> reads it; sform story get <rst> -p <org>.<other>
    // targets the other project and stops before any call with 2 and "The machine credentials in STORYTELLER_API_KEY belong
    // to <org>.<project>, not <org>.<other>."
    [Test]
    [TestCase(244)]
    [MachineOnly]
    [RequiresMachine(MachineCatalog.ApiKeyMachine)]
    [Covers("story get", "-p")]
    public async Task Machine_StorytellerKey_SetsTarget()
    {
        var configured = MachineOf(MachineCatalog.ApiKeyMachine);
        var apiKey = configured.Variables[MachineCredentials.ApiKeyVariable]!;
        var project = Machines[MachineCatalog.ApiKeyMachine].Project ?? MachineCredentials.ProjectKeyOf(apiKey) ?? Target.ProjectKey;
        var organization = project[..project.IndexOf('.', StringComparison.Ordinal)];
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();

        var editor = await ArrangeAsEditorAsync();
        (await editor.Sform("story", "set", key, "-p", project, "-v", view)).ShouldSucceed();

        var machine = configured.With(new Dictionary<string, string?>
        {
            [MachineCredentials.Project] = null,
            [MachineCredentials.View] = null,
            [MachineCredentials.Key] = $"@{project}.{view}.{key}",
        });

        var read = (await Sform(AsMachine(machine), "story", "get", key)).ShouldSucceed();
        await Assert.That(read.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        var other = $"{organization}.{CliData.ProjectName()}";
        (await Sform(AsMachine(machine), "story", "get", key, "-p", other))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The machine credentials in {MachineCredentials.ApiKeyVariable} belong to {project}, not {other}.", comparison: StringComparison.OrdinalIgnoreCase);
    }

    // SGN-MSI-03: Wrong machine credentials fail on the first call with exit 44 and name the variable.
    // As the configured API key machine with the last character of the key's secret changed (its machine segment, and so
    // its project, stays): sform account ends with 0 and "The API checks the credentials on the first call.", as nothing
    // checks the key before a call; sform story get <rst> ends with 44, "You are not authorized to execute the command or
    // you don't have access to requested resources." and "sform signed in as a machine with STORYTELLER_API_KEY. …", which
    // names the variable and neither key.
    [Test]
    [TestCase(245)]
    [MachineOnly]
    [RequiresMachine(MachineCatalog.ApiKeyMachine)]
    [Covers("account")]
    public async Task Machine_WrongCredentials_Unauthorized()
    {
        var configured = MachineOf(MachineCatalog.ApiKeyMachine);
        var apiKey = configured.Variables[MachineCredentials.ApiKeyVariable]!;
        var wrong = SignInOutput.Tampered(apiKey);
        var machine = configured.With(new Dictionary<string, string?> { [MachineCredentials.ApiKeyVariable] = wrong }, [wrong]);

        (await Sform(AsMachine(machine), "account"))
            .ShouldSucceed()
            .ShouldContain("with an API key (STORYTELLER_API_KEY).")
            .ShouldContain("The API checks the credentials on the first call.");

        (await Sform(AsMachine(machine), "story", "get", CliData.ResponsibilityKey()))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain(GeneralRefusal)
            .ShouldContain($"sform signed in as a machine with {MachineCredentials.ApiKeyVariable}.")
            .ShouldNotContain(wrong)
            .ShouldNotContain(wrong, SformStream.StdErr)
            .ShouldNotContain(apiKey)
            .ShouldNotContain(apiKey, SformStream.StdErr);
    }
}
