using System.Globalization;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// Set, Patch and Delete: sform story config set, patch and delete.
[Area(Area.Configurations)]
public sealed class ConfigWriteTests : CliTest
{
    // A type schema of rst in a view: a must be a number.
    private const string ANumberSchema = """{ "type": "object", "properties": { "a": { "type": "number" } } }""";

    // CFG-SET-01: config set -x merges inline properties into the stored content.
    // As an Editor, config set <rst> -x a=1 -x b.c=true -x "['e.f']=2" stores a=1 (a number), b as the object
    // { "c": true } (the name of -x is a JSON path, so b.c nests) and a property named "e.f" with 2 (a name with a dot is
    // written in ['…']); config set <rst> --properties d=x merges d="x" into it as a new version, keeping a, b.c and e.f
    // (its printed model has the stored content), and story config <rst> --content-only returns the merged content, with
    // no property named "b.c". The model the first write prints has no Content: the API answers the create of a
    // configuration with its calculated content, which a new configuration does not have yet
    // (CosmosConfigurationService.WriteConfigurationAsync returns ToConfiguration() there, ToConfigurationFromContent()
    // on every later write).
    [Test]
    [TestCase(395)]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story config set", "arg0", "-x", "--properties")]
    public async Task Config_SetProperties_Merges()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        var first = (await Sform(InProject("story", "config", "set", key, "-x", "a=1", "-x", "b.c=true", "-x", "['e.f']=2"))).ShouldSucceed().JsonNode();
        var firstVersion = first["Version"]!.GetValue<int>();

        var second = (await Sform(InProject("story", "config", "set", key, "--properties", "d=x"))).ShouldSucceed().JsonNode();
        await Assert.That(second["Version"]!.GetValue<int>()).IsGreaterThan(firstVersion);
        await AssertMergedAsync(second["Content"]);

        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await AssertMergedAsync(content);
    }

    // CFG-SET-02: config create is an alias of set.
    // As an Editor, config create <rst> -x e=2 stores e=2 as config set does: it prints the model of the first version,
    // and config <rst> returns e=2.
    [Test]
    [TestCase(397)]
    [UserRole(Role.Editor)]
    [Covers("story config create")]
    public async Task Config_CreateAlias_Sets()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        var created = (await Sform(InProject("story", "config", "create", key, "-x", "e=2"))).ShouldSucceed().JsonNode();
        await Assert.That(created["Version"]?.GetValue<long>() ?? 0).IsGreaterThanOrEqualTo(1L);

        var read = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(read["Content"]?["e"]?.GetValue<long>()).IsEqualTo(2L);
    }

    // CFG-SET-03: --replace removes the properties that are not given.
    // As an Editor, with a=1 and b=2 stored on <rst>, config set <rst> -x only=1 --replace stores { "only": 1 } as a new
    // version (the printed model has that content alone), and config <rst> no longer has a or b.
    [Test]
    [TestCase(399)]
    [UserRole(Role.Editor)]
    [Covers("story config set", "--replace")]
    public async Task Config_Replace_RemovesOthers()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        var before = (await Sform(InProject("story", "config", "set", key, "-x", "a=1", "-x", "b=2"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();

        var replaced = (await Sform(InProject("story", "config", "set", key, "-x", "only=1", "--replace"))).ShouldSucceed().JsonNode();
        var stored = replaced["Content"]!.AsObject();
        await Assert.That(stored.Count).IsEqualTo(1);
        await Assert.That(stored["only"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(replaced["Version"]!.GetValue<long>()).IsGreaterThan(before);

        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["only"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(content["a"]).IsNull();
        await Assert.That(content["b"]).IsNull();
    }

    // CFG-SET-04: -i imports the content from a file.
    // As an Editor, config set <rst> -i c.json and config set <rst> --import c2.json merge both files into the stored
    // content: a and nested.x of the first, b and nested.y of the second.
    [Test]
    [TestCase(400)]
    [UserRole(Role.Editor)]
    [Covers("story config set", "-i", "--import")]
    public async Task Config_Import_FromFile()
    {
        var key = CliData.ResponsibilityKey();
        var value = CliData.Value();
        await WriteFileAsync("c.json", $$"""{ "a": 1, "nested": { "x": "{{value}}" } }""");
        await WriteFileAsync("c2.json", """{ "b": 2, "nested": { "y": true } }""");
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        (await Sform(InProject("story", "config", "set", key, "-i", "c.json"))).ShouldSucceed();
        var imported = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(imported["a"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(imported["nested"]?["x"]?.GetValue<string>()).IsEqualTo(value);
        (await Sform(InProject("story", "config", "set", key, "--import", "c2.json"))).ShouldSucceed();

        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["a"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(content["b"]?.GetValue<long>()).IsEqualTo(2L);
        await Assert.That(content["nested"]?["x"]?.GetValue<string>()).IsEqualTo(value);
        await Assert.That(content["nested"]?["y"]?.GetValue<bool>()).IsTrue();
    }

    // CFG-SET-05: config set has no -c or -l: they end with exit -1 and store nothing.
    // As an Editor, with a=1 stored on <rst>, config set <rst> -x a=2 with -c owner=t, --custom-properties n=2, -l x or
    // --labels y each end with -1 and "Unrecognized option" (the parser stops before the command runs), and the
    // configuration keeps its version and a=1: custom properties and labels belong to the annotation (story set -c|-l).
    [Test]
    [TestCase(401)]
    [UserRole(Role.Editor)]
    [Covers("story config set")]
    public async Task Config_CustomPropertiesAndLabels_Rejected()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        var version = (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();

        string[][] rejected = [["-c", "owner=t"], ["--custom-properties", "n=2"], ["-l", "x"], ["--labels", "y"]];
        foreach (var options in rejected)
        {
            (await Sform(InProject(["story", "config", "set", key, "-x", "a=2", .. options])))
                .ShouldExitWith(CliExit.InputParsing)
                .ShouldContain("Unrecognized option");
        }

        var read = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(read["Version"]?.GetValue<long>()).IsEqualTo(version);
        await Assert.That(read["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);
    }

    // CFG-SET-06: set -r prints the resolved configuration after the write.
    // As an Editor, with a configuration of <rst> (inherited=<v>) and its unit <unt>, config set <unt> -x a=1 -r and
    // --resolved print the resolved configuration of the unit: a=1 and the inherited value; without -r the printed
    // model is the stored content of the unit alone, without the inherited value.
    [Test]
    [TestCase(402)]
    [UserRole(Role.Editor)]
    [Covers("story config set", "-r", "--resolved")]
    public async Task Config_SetResolved_PrintsResolved()
    {
        var parent = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(parent);
        var inherited = CliData.Value();
        (await Sform(InProject("story", "set", parent))).ShouldSucceed();
        (await Sform(InProject("story", "set", unit))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", parent, "-x", $"inherited={inherited}"))).ShouldSucceed();

        foreach (var option in new[] { "-r", "--resolved" })
        {
            var printed = (await Sform(InProject("story", "config", "set", unit, "-x", "a=1", option))).ShouldSucceed().JsonNode()["Content"];
            await Assert.That(printed?["a"]?.GetValue<long>()).IsEqualTo(1L);
            await Assert.That(printed?["inherited"]?.GetValue<string>()).IsEqualTo(inherited);
        }

        var stored = (await Sform(InProject("story", "config", "set", unit, "-x", "b=2"))).ShouldSucceed().JsonNode()["Content"];
        await Assert.That(stored?["b"]?.GetValue<long>()).IsEqualTo(2L);
        await Assert.That(stored?["inherited"]).IsNull();
    }

    // CFG-SET-07: A content that violates the schema fails, and -f stores it.
    // As an Editor, in a generated view of the project (a type schema there constrains no other test) with a type schema
    // of rst that requires a to be a number and a=1 stored on <rst>: config set <rst> -x a=text ends with 2 and the
    // violation ("<rst> (<view>)" and the messages) and stores no version; config set <rst> -x a=text -f stores it as a
    // new version, printed as the model without a warning; config set <rst> -x a=other --force stores another version
    // (a write of the stored content again would store none).
    [Test]
    [TestCase(403)]
    [UserRole(Role.Editor)]
    [Covers("story config set", "-f", "--force")]
    public async Task Config_SchemaViolation_Force()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        await WriteFileAsync("a-number.json", ANumberSchema);
        (await Sform(At("story", "schema", "type", "set", "rst", "-i", "a-number.json"))).ShouldSucceed();
        (await Sform(At("story", "set", key))).ShouldSucceed();
        var stored = (await Sform(At("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();

        (await Sform(At("story", "config", "set", key, "-x", "a=text")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"{key} ({view})");
        await Assert.That((await Sform(At("story", "config", key))).ShouldSucceed().JsonNode()["Version"]?.GetValue<long>()).IsEqualTo(stored);

        var forced = (await Sform(At("story", "config", "set", key, "-x", "a=text", "-f"))).ShouldSucceed().ShouldNotContain("Warning");
        var forcedModel = forced.JsonNode();
        await Assert.That(forcedModel["Content"]?["a"]?.GetValue<string>()).IsEqualTo("text");
        await Assert.That(forcedModel["Version"]!.GetValue<long>()).IsGreaterThan(stored);

        var again = (await Sform(At("story", "config", "set", key, "-x", "a=other", "--force"))).ShouldSucceed().ShouldNotContain("Warning").JsonNode();
        await Assert.That(again["Content"]?["a"]?.GetValue<string>()).IsEqualTo("other");
        await Assert.That(again["Version"]!.GetValue<long>()).IsGreaterThan(forcedModel["Version"]!.GetValue<long>());

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // CFG-SET-08: config patch -i -y applies a JSON Patch.
    // As an Editor, with a=1 and b=true stored on <rst>: config patch <rst> -i p.json -y (replace a, add c, remove b)
    // prints the JSON Patch, "Configuration for '<rst>' patched: version N → M." and the model with a=2, c="x" and no b;
    // config patch <rst> --import p2.json --yes (a=3) stores another version; config <rst> has a=3 and c.
    [Test]
    [TestCase(404)]
    [UserRole(Role.Editor)]
    [Covers("story config patch", "arg0", "-i", "--import", "-y", "--yes")]
    public async Task Config_Patch_Applies()
    {
        var key = CliData.ResponsibilityKey();
        await WriteFileAsync("p.json", """[ { "op": "replace", "path": "/a", "value": 2 }, { "op": "add", "path": "/c", "value": "x" }, { "op": "remove", "path": "/b" } ]""");
        await WriteFileAsync("p2.json", """[ { "op": "replace", "path": "/a", "value": 3 } ]""");
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        var before = (await Sform(InProject("story", "config", "set", key, "-x", "a=1", "-x", "b=true"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();

        var first = (await Sform(InProject("story", "config", "patch", key, "-i", "p.json", "-y")))
            .ShouldSucceed()
            .ShouldContain("JSON Patch")
            .ShouldContain($"Configuration for '{key}' patched: version {Text(before)} ");
        var patched = ConfigOutput.LastJson(first);
        await Assert.That(patched["Content"]?["a"]?.GetValue<long>()).IsEqualTo(2L);
        await Assert.That(patched["Content"]?["c"]?.GetValue<string>()).IsEqualTo("x");
        await Assert.That(patched["Content"]?["b"]).IsNull();
        var middle = patched["Version"]!.GetValue<long>();
        await Assert.That(middle).IsGreaterThan(before);

        var second = (await Sform(InProject("story", "config", "patch", key, "--import", "p2.json", "--yes")))
            .ShouldSucceed()
            .ShouldContain($"Configuration for '{key}' patched: version {Text(middle)} ");
        await Assert.That(ConfigOutput.LastJson(second)["Version"]!.GetValue<long>()).IsGreaterThan(middle);

        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["a"]?.GetValue<long>()).IsEqualTo(3L);
        await Assert.That(content["c"]?.GetValue<string>()).IsEqualTo("x");
        await Assert.That(content["b"]).IsNull();
    }

    // CFG-SET-09: patch shows the change and asks in a terminal; No aborts with exit 40.
    // As an Editor, with a=1 stored on <rst>, config patch <rst> -i p.json in a pseudo terminal shows the patch and asks
    // "Apply this patch to '<rst>'"; n ends with 40 and "Patch aborted.", and the configuration keeps its version and a=1;
    // the same run answered with y applies it ("… patched: version N → M.") and a=2 is stored. The texts on the screen are
    // expected with ExpectAsync, which takes a cursor move of the pseudo terminal for the spaces it replaces.
    [Test]
    [TestCase(405)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story config patch")]
    public async Task Config_Patch_ConfirmOrAbort()
    {
        var key = CliData.ResponsibilityKey();
        await WriteFileAsync("p.json", """[ { "op": "replace", "path": "/a", "value": 2 } ]""");
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        var before = (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();

        await using (var declined = await StartSformInTerminal(InProject("story", "config", "patch", key, "-i", "p.json")))
        {
            await declined.ExpectAsync($"Apply this patch to '{key}'");
            await declined.SendLineAsync("n");
            await declined.ExpectAsync("Patch aborted.");
            (await declined.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted);
        }

        var unchanged = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(unchanged["Version"]?.GetValue<long>()).IsEqualTo(before);
        await Assert.That(unchanged["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);

        await using (var accepted = await StartSformInTerminal(InProject("story", "config", "patch", key, "-i", "p.json")))
        {
            await accepted.ExpectAsync($"Apply this patch to '{key}'");
            await accepted.SendLineAsync("y");
            await accepted.ExpectAsync($"Configuration for '{key}' patched: version {Text(before)}");
            (await accepted.WaitForExitAsync()).ShouldSucceed();
        }

        var applied = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(applied["Content"]?["a"]?.GetValue<long>()).IsEqualTo(2L);
        await Assert.That(applied["Version"]!.GetValue<long>()).IsGreaterThan(before);
    }

    // CFG-SET-10: patch -f stores a patched content that violates the schema; a failing test op changes nothing.
    // As an Editor, in a generated view with a type schema of rst that requires a to be a number and a=1 stored on <rst>
    // and <rst2>: config patch <rst> -i break.json -y (a becomes a text) ends with 2 and the violation; -y -f on <rst>
    // and --yes --force on <rst2> store it ("… patched: version N → M." and the model, no warning); config patch <rst>
    // -i failing-test.json -y, whose test op does not match, ends with 2, "Error code: PatchTestFailed" and the server's
    // message (412), and <rst> keeps its version.
    [Test]
    [TestCase(406)]
    [UserRole(Role.Editor)]
    [Covers("story config patch", "-f", "--force")]
    public async Task Config_Patch_ForceAndTest()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var key2 = CliData.ResponsibilityKey();
        await WriteFileAsync("a-number.json", ANumberSchema);
        await WriteFileAsync("break.json", """[ { "op": "replace", "path": "/a", "value": "text" } ]""");
        await WriteFileAsync("failing-test.json", """[ { "op": "test", "path": "/a", "value": 424242 }, { "op": "replace", "path": "/a", "value": 7 } ]""");
        (await Sform(At("story", "schema", "type", "set", "rst", "-i", "a-number.json"))).ShouldSucceed();
        (await Sform(At("story", "set", key))).ShouldSucceed();
        (await Sform(At("story", "set", key2))).ShouldSucceed();
        var before = (await Sform(At("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        var before2 = (await Sform(At("story", "config", "set", key2, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();

        (await Sform(At("story", "config", "patch", key, "-i", "break.json", "-y")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"{key} ({view})");

        var forced = (await Sform(At("story", "config", "patch", key, "-i", "break.json", "-y", "-f")))
            .ShouldSucceed()
            .ShouldContain($"Configuration for '{key}' patched: version {Text(before)} ")
            .ShouldNotContain("Warning");
        await Assert.That(ConfigOutput.LastJson(forced)["Content"]?["a"]?.GetValue<string>()).IsEqualTo("text");
        var after = ConfigOutput.LastJson(forced)["Version"]!.GetValue<long>();

        var forced2 = (await Sform(At("story", "config", "patch", key2, "-i", "break.json", "--yes", "--force")))
            .ShouldSucceed()
            .ShouldContain($"Configuration for '{key2}' patched: version {Text(before2)} ")
            .ShouldNotContain("Warning");
        await Assert.That(ConfigOutput.LastJson(forced2)["Content"]?["a"]?.GetValue<string>()).IsEqualTo("text");

        // the server's message names the failing operation (the test op is operation 0 of the file)
        (await Sform(At("story", "config", "patch", key, "-i", "failing-test.json", "-y")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("JSON Patch operation 0 failed:")
            .ShouldContain("Error code: PatchTestFailed");
        var kept = (await Sform(At("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(kept["Version"]?.GetValue<long>()).IsEqualTo(after);
        await Assert.That(kept["Content"]?["a"]?.GetValue<string>()).IsEqualTo("text");

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // CFG-SET-11: config delete (and remove) deletes the configuration.
    // As an Editor, config delete <rst> and config remove <rst2> say "The configuration for '<key>' has been deleted.",
    // and config <rst> and config <rst2> no longer have the deleted values (the annotations stay, with no stored
    // configuration).
    [Test]
    [TestCase(407)]
    [UserRole(Role.Editor)]
    [Covers("story config delete", "arg0")]
    [Covers("story config remove", "arg0")]
    public async Task Config_Delete_Removes()
    {
        var key = CliData.ResponsibilityKey();
        var key2 = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        foreach (var annotation in new[] { key, key2 })
        {
            (await Sform(InProject("story", "set", annotation))).ShouldSucceed();
            (await Sform(InProject("story", "config", "set", annotation, "-x", $"probe={probe}"))).ShouldSucceed();
        }

        (await Sform(InProject("story", "config", "delete", key))).ShouldSucceed().ShouldContain($"The configuration for '{key}' has been deleted.");
        (await Sform(InProject("story", "config", "remove", key2))).ShouldSucceed().ShouldContain($"The configuration for '{key2}' has been deleted.");

        foreach (var annotation in new[] { key, key2 })
        {
            var content = (await Sform(InProject("story", "config", annotation, "--content-only"))).ShouldSucceed().ShouldNotContain(probe).JsonNode();
            await Assert.That(content["probe"]).IsNull();
        }
    }

    // CFG-SET-12: A Reader's config set is refused and stores nothing.
    // As a Reader of the project (Target:RoleProjects:Reader, or a Reader of Target:Project), config set <rst> -x a=1
    // ends with 44, the server's message ("No Contributor access to '<project>'.") and "Error code: AccessDenied"; nothing
    // was stored: config <rst> finds no configuration (2).
    [Test]
    [TestCase(408)]
    [UserRole(Role.Reader)]
    [Covers("story config set")]
    public async Task Config_SetAsReader_Refused()
    {
        var key = CliData.ResponsibilityKey();

        (await Sform(InProject("story", "config", "set", key, "-x", "a=1")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Contributor access to '{ProjectKey}'.")
            .ShouldContain("Error code: AccessDenied");

        (await Sform(InProject("story", "config", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The configuration for '{key}' has not been found.");
    }

    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);

    // CFG-SET-01: the content after both writes: a=1, b={ "c": true } (no property named "b.c"), "e.f"=2 and d="x".
    private static async Task AssertMergedAsync(System.Text.Json.Nodes.JsonNode? content)
    {
        await Assert.That(content?["a"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(content?["b"]?["c"]?.GetValue<bool>()).IsTrue();
        await Assert.That(content?["b.c"]).IsNull();
        await Assert.That(content?["e.f"]?.GetValue<long>()).IsEqualTo(2L);
        await Assert.That(content?["d"]?.GetValue<string>()).IsEqualTo("x");
    }
}
