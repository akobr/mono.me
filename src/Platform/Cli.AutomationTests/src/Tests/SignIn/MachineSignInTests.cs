using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
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
}
