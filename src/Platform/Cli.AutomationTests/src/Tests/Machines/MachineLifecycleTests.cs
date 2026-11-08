using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// Machine Lifecycle: machines of the project, created, reset and deleted through sform account machine.
[Area(Area.Machines)]
public sealed class MachineLifecycleTests : CliTest
{
    // MCH-LIF-03: A created API key machine signs in with its key and reads with the default read-only scopes.
    // As an Editor, sform account machine create -r in the project prints the machine (its id, the key once, the kind
    // ApiKey and the scopes AnnotationRead and ConfigurationRead, nothing that writes), says to copy the access key and
    // lists the STORYTELLER_* variables of a pipeline; with that key in the STORYTELLER_* variables, sform account signs
    // the machine in, and sform story get reads an annotation the Editor created. The machine is deleted when the test
    // ends.
    [Test]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "-r")]
    public async Task Machine_CreateApiKey_ReadsOnly()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        var machine = await CreateMachineAsync("-r");

        await Assert.That(machine.CredentialKind).IsEqualTo("ApiKey");
        await Assert.That(machine.AccessKey).IsNotNull();
        await Assert.That(machine.Scopes.Select(Normalize)).IsEquivalentTo(new[] { "annotationread", "configurationread" });
        machine.Result
            .ShouldContain("Make sure to copy the access key (secret), it is not stored anywhere.")
            .ShouldContain("To sign in sform in a pipeline, set:")
            .ShouldContain($"STORYTELLER_ENDPOINT={Target.BaseUrl}")
            .ShouldContain("STORYTELLER_API_KEY=<access key above>");

        var asMachine = AsMachine(machine.ApiKeyCredentials);
        (await Sform(asMachine, "account"))
            .ShouldSucceed()
            .ShouldContain($"Signed in as machine {machine.Id} with an API key")
            .ShouldContain($"Project {ProjectKey} with {View} view.");

        var read = (await Sform(asMachine, "story", "get", key)).ShouldSucceed();
        await Assert.That(read.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
    }

    // AnnotationRead, Annotation.Read and annotation-read are the same scope.
    private static string Normalize(string scope)
    {
        return new string(scope.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
