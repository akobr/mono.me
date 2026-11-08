using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// Get and Set: sform story set and sform story get of single annotations.
[Area(Area.Annotations)]
public sealed class AnnotationEditTests : CliTest
{
    // ANN-SET-01: story set creates an annotation, and story get returns it.
    // As an Editor, story set <rst> in the project prints the new annotation, and story get <rst> returns it with its key,
    // its type (Responsibility) and its name (the part after rst.), in the project and view of the run.
    [Test]
    [TestCase(275)]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story set", "arg0")]
    [Covers("story get", "arg0")]
    public async Task Story_SetGet_RoundTrip()
    {
        var key = CliData.ResponsibilityKey();

        var set = (await Sform(InProject("story", "set", key))).ShouldSucceed();
        await Assert.That(set.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
        await Assert.That(annotation["AnnotationType"]?.GetValue<string>()).IsEqualTo("Responsibility");
        await Assert.That(annotation["Name"]!.GetValue<string>()).IsEqualTo(key["rst.".Length..]);
        await Assert.That(annotation["ProjectName"]!.GetValue<string>()).IsEqualTo(Project);
        await Assert.That(annotation["ViewName"]!.GetValue<string>()).IsEqualTo(View);
    }

    // ANN-SET-02: story create is an alias of set, and a second set updates.
    // As an Editor, story create <rst> creates the annotation; story set <rst> -l a then updates the same annotation:
    // story get <rst> has the label a, and its history (story history --json) has one Created and one Updated entry, no
    // second creation.
    [Test]
    [TestCase(276)]
    [UserRole(Role.Editor)]
    [Covers("story create")]
    public async Task Story_CreateAlias_Updates()
    {
        var key = CliData.ResponsibilityKey();

        var created = (await Sform(InProject("story", "create", key))).ShouldSucceed().JsonNode();
        await Assert.That(created["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        var updated = (await Sform(InProject("story", "set", key, "-l", "a"))).ShouldSucceed().JsonNode();
        await Assert.That(Strings(updated["Labels"])).IsEquivalentTo(new[] { "a" });

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
        await Assert.That(Strings(annotation["Labels"])).IsEquivalentTo(new[] { "a" });

        var history = (await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode();
        await Assert.That(Actions(history)).IsEquivalentTo(new[] { "Created", "Updated" });
    }

    // ANN-SET-03: -c sets custom properties with typed values, and -l sets labels.
    // As an Editor, story set <rst> -c owner=team-a -c count=3 -c on=true -c since=2026-11-01
    // --custom-properties ttl=1:30:00 -l x -l y --labels z; story get <rst> returns owner as the string team-a, count as
    // the number 3, on as the boolean true, since as the date 2026-11-01 (with the time and offset sform sent), ttl as the
    // duration 01:30:00, and the labels x, y and z.
    [Test]
    [TestCase(278)]
    [UserRole(Role.Editor)]
    [Covers("story set", "-c", "--custom-properties", "-l", "--labels")]
    public async Task Story_CustomPropertiesAndLabels()
    {
        var key = CliData.ResponsibilityKey();

        string[] set =
        [
            "story", "set", key,
            "-c", "owner=team-a",
            "-c", "count=3",
            "-c", "on=true",
            "-c", "since=2026-11-01",
            "--custom-properties", "ttl=1:30:00",
            "-l", "x",
            "-l", "y",
            "--labels", "z",
        ];
        (await Sform(InProject(set))).ShouldSucceed();

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        var values = annotation["Values"]!;
        await Assert.That(values["owner"]!.GetValueKind()).IsEqualTo(JsonValueKind.String);
        await Assert.That(values["owner"]!.GetValue<string>()).IsEqualTo("team-a");
        await Assert.That(values["count"]!.GetValueKind()).IsEqualTo(JsonValueKind.Number);
        await Assert.That(values["count"]!.GetValue<double>()).IsEqualTo(3d);
        await Assert.That(values["on"]!.GetValueKind()).IsEqualTo(JsonValueKind.True);
        await Assert.That(values["since"]!.GetValue<string>()).StartsWith("2026-11-01");
        await Assert.That(values["ttl"]!.GetValue<string>()).IsEqualTo("01:30:00");
        await Assert.That(Strings(annotation["Labels"])).IsEquivalentTo(new[] { "x", "y", "z" });
    }

    // ANN-SET-04: -i imports one annotation from a file under the given key.
    // As an Editor, one.json and two.json each hold an annotation object of another key and name, with a title, custom
    // properties and labels; story set <rst> -i one.json and story set <rst2> --import two.json store each file's title,
    // properties and labels under the key of the argument (and its name), and the key of the file is not created.
    [Test]
    [TestCase(284)]
    [UserRole(Role.Editor)]
    [Covers("story set", "-i", "--import")]
    public async Task Story_Import_FromFile()
    {
        foreach (var option in new[] { "-i", "--import" })
        {
            var key = CliData.ResponsibilityKey();
            var decoy = CliData.AnnotationPart();
            var title = CliData.Value();
            var tier = CliData.Value();
            var label = CliData.LabelName();
            var file = option == "-i" ? "one.json" : "two.json";
            var content = new JsonObject
            {
                ["AnnotationKey"] = $"{CliData.Responsibility}.{decoy}",
                ["Name"] = decoy,
                ["Title"] = title,
                ["Labels"] = new JsonArray(label),
                ["Values"] = new JsonObject { ["tier"] = tier, ["count"] = 2 },
            };
            await WriteFileAsync(file, content.ToJsonString());

            var set = (await Sform(InProject("story", "set", key, option, file))).ShouldSucceed().JsonNode();
            await Assert.That(set["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

            var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
            await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
            await Assert.That(annotation["Name"]!.GetValue<string>()).IsEqualTo(key["rst.".Length..]);
            await Assert.That(annotation["Title"]?.GetValue<string>()).IsEqualTo(title);
            await Assert.That(annotation["Values"]?["tier"]?.GetValue<string>()).IsEqualTo(tier);
            await Assert.That(annotation["Values"]?["count"]?.GetValue<double>()).IsEqualTo(2d);
            await Assert.That(Strings(annotation["Labels"])).IsEquivalentTo(new[] { label });

            (await Sform(InProject("story", "get", $"{CliData.Responsibility}.{decoy}"))).ShouldExitWith(CliExit.WrongInput);
        }
    }

    // ANN-SET-05: get -e saves the annotation into a file.
    // An Editor creates <rst>; as a Reader, story get <rst> -e a.json and story get <rst> --export out/b.json write the
    // annotation JSON (its key) into the files; after the Editor labels <rst>, story get <rst> -e a.json again (the input
    // is redirected) rewrites a.json without asking, says "The file 'a.json' already exists and is rewritten." and the
    // file has the label.
    [Test]
    [TestCase(286)]
    [UserRole(Role.Reader)]
    [Covers("story get", "-e", "--export")]
    public async Task Story_GetExport_WritesFile()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var label = CliData.LabelName();
        await editor.Sform(InProject("story", "set", key)).ShouldSucceed();
        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(WorkingDirectory, "out"));

        (await Sform(InProject("story", "get", key, "-e", "a.json")))
            .ShouldSucceed()
            .ShouldNotContain("already exists");
        await Assert.That(JsonNode.Parse(await ReadFileAsync("a.json"))!["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        (await Sform(InProject("story", "get", key, "--export", "out/b.json"))).ShouldSucceed();
        await Assert.That(JsonNode.Parse(await ReadFileAsync("out/b.json"))!["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        await editor.Sform(InProject("story", "set", key, "-l", label)).ShouldSucceed();
        (await Sform(InProject("story", "get", key, "-e", "a.json")))
            .ShouldSucceed()
            .ShouldContain("The file 'a.json' already exists and is rewritten.");
        var rewritten = JsonNode.Parse(await ReadFileAsync("a.json"))!;
        await Assert.That(rewritten["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
        await Assert.That(Strings(rewritten["Labels"])).Contains(label);
    }

    // ANN-SET-06: Setting a deleted annotation fails without an option, --restore-deleted restores it, --recreate creates
    // it new.
    // As an Editor, <rst> gets the label before and a configuration (config set -x cli_probe=<value>), and is deleted.
    // story set <rst> on the redirected input lists the deleted annotation and ends with 2; its standard error has "! sform
    // cannot ask for what happens to the deleted annotations: the input is redirected." and names --restore-deleted and
    // --recreate. story set <rst> --restore-deleted -l r says "Restored <rst>." and merges the label into the restored
    // annotation (one PATCH after the restore): the labels are before and r; the configuration is back and the history
    // keeps the entries from before the delete. After another delete, story set <rst> --recreate creates it new: no
    // "Restored", no label before, and the configuration no longer has cli_probe.
    [Test]
    [TestCase(290)]
    [UserRole(Role.Editor)]
    [Covers("story set", "--restore-deleted", "--recreate")]
    public async Task Story_SetDeleted_RestoreOrRecreate()
    {
        var key = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        await Sform(InProject("story", "set", key, "-l", "before")).ShouldSucceed();
        await Sform(InProject("story", "config", "set", key, "-x", $"cli_probe={probe}")).ShouldSucceed();
        await Sform(InProject("story", "delete", key)).ShouldSucceed();

        (await Sform(InProject("story", "set", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("This annotation is deleted:")
            .ShouldContain(key)
            .ShouldContain("! sform cannot ask for what happens to the deleted annotations: the input is redirected.", SformStream.StdErr)
            .ShouldContain("--restore-deleted", SformStream.StdErr)
            .ShouldContain("--recreate", SformStream.StdErr);

        var restored = (await Sform(InProject("story", "set", key, "--restore-deleted", "-l", "r")))
            .ShouldSucceed()
            .ShouldContain($"Restored {key}.");
        await Assert.That(string.Join(", ", Strings(restored.JsonNode()["Labels"]))).IsEqualTo("before, r");
        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["cli_probe"]?.GetValue<string>()).IsEqualTo(probe);
        var history = (await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode();
        var actions = Actions(history);
        await Assert.That(actions).Contains("Created");
        await Assert.That(actions).Contains("Deleted");
        await Assert.That(actions).Contains("Restored");

        await Sform(InProject("story", "delete", key)).ShouldSucceed();
        var created = (await Sform(InProject("story", "set", key, "--recreate")))
            .ShouldSucceed()
            .ShouldContain("This annotation is deleted:")
            .ShouldNotContain("Restored ");
        await Assert.That(Strings(created.JsonNode()["Labels"])).DoesNotContain("before");
        var recreated = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(recreated["cli_probe"]).IsNull();
    }

    // ANN-SET-07: An unknown key fails with exit 2 and a not-found message; an invalid key is refused by the API.
    // story get rst.<generated> ends with 2 and "The annotation 'rst.<generated>' has not been found."; story get
    // not-a-key ends with 2, the API's "Invalid annotation key: not-a-key" and "Error code: InvalidKey".
    [Test]
    [TestCase(292)]
    [UserRole(Role.Reader)]
    [Covers("story get", "arg0")]
    public async Task Story_InvalidOrUnknownKey_Fails()
    {
        var unknown = CliData.ResponsibilityKey();

        (await Sform(InProject("story", "get", unknown)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{unknown}' has not been found.");

        (await Sform(InProject("story", "get", "not-a-key")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Invalid annotation key: not-a-key")
            .ShouldContain("Error code: InvalidKey");
    }

    // ANN-SET-08: A Reader's story set is refused with exit 44.
    // As a Reader of the project (Target:RoleProjects:Reader, or a Reader of Target:Project), story set <rst> ends with 44
    // and creates nothing: story get <rst> finds no annotation.
    [Test]
    [TestCase(293)]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("story set")]
    public async Task Story_SetAsReader_Unauthorized()
    {
        var key = CliData.ResponsibilityKey();

        (await Sform(InProject("story", "set", key))).ShouldExitWith(CliExit.Unauthorized);

        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");
    }

    // ANN-SET-09: A machine with Annotation.Write sets, and without it is refused.
    // An Editor creates two API key machines of the project, one with -w and one with -r (both deleted at the end). As
    // the -w machine, story set <rst> creates <rst>; as the -r machine, story set <rst2> ends with 44 and the server's
    // message names Annotation.Write, and <rst2> does not exist.
    [Test]
    [TestCase(294)]
    [MachineOnly]
    [Covers("story set")]
    public async Task Story_MachineScopes_Decide()
    {
        var creator = await ArrangeAsEditorAsync();
        var writer = await CreateMachineAsync(new MachineCreation { As = creator }, "-w", "-k", "ApiKey");
        var reader = await CreateMachineAsync(new MachineCreation { As = creator }, "-r", "-k", "ApiKey");
        var key = CliData.ResponsibilityKey();
        var refused = CliData.ResponsibilityKey();

        var set = (await Sform(AsMachine(writer.SignInCredentials), "story", "set", key)).ShouldSucceed();
        await Assert.That(set.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        (await Sform(AsMachine(reader.SignInCredentials), "story", "set", refused))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("Annotation.Write");

        (await creator.Sform(InProject("story", "get", refused)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{refused}' has not been found.");
    }

    // ANN-SET-10: story set merges into an existing annotation, and --replace replaces its whole content.
    // As an Editor, story set <rst> -i full.json creates <rst> with a title, a description, the label x and the values a=1
    // and b=2. story set <rst> -l y -c a=3 merges with one PATCH: the title and the description stay, the labels are x and
    // y, a is 3 and b stays 2. story set <rst> --replace -c a=4 -l z writes the options alone (with If-Match of the version
    // it read): no title, no description, the label z only and the value a=4 only. story set <rst2> --replace -c a=1
    // creates the missing <rst2>. The history of <rst> is Created, Updated, Updated.
    [Test]
    [TestCase(472)]
    [UserRole(Role.Editor)]
    [Covers("story set", "--replace")]
    public async Task Story_SetReplace_ClearsWhatTheInputLacks()
    {
        var key = CliData.ResponsibilityKey();
        var missing = CliData.ResponsibilityKey();
        var title = CliData.Value();
        var description = CliData.Value();
        await WriteFileAsync("full.json", new JsonObject
        {
            ["Title"] = title,
            ["Description"] = description,
            ["Labels"] = new JsonArray("x"),
            ["Values"] = new JsonObject { ["a"] = 1, ["b"] = 2 },
        }.ToJsonString());
        (await Sform(InProject("story", "set", key, "-i", "full.json"))).ShouldSucceed();

        var merged = (await Sform(InProject("story", "set", key, "-l", "y", "-c", "a=3"))).ShouldSucceed().JsonNode();
        await Assert.That(merged["Title"]?.GetValue<string>()).IsEqualTo(title);
        await Assert.That(merged["Description"]?.GetValue<string>()).IsEqualTo(description);
        await Assert.That(string.Join(", ", Strings(merged["Labels"]))).IsEqualTo("x, y");
        await Assert.That(merged["Values"]?["a"]?.GetValue<double>()).IsEqualTo(3d);
        await Assert.That(merged["Values"]?["b"]?.GetValue<double>()).IsEqualTo(2d);

        (await Sform(InProject("story", "set", key, "--replace", "-c", "a=4", "-l", "z"))).ShouldSucceed();
        var replaced = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(replaced["Title"]).IsNull();
        await Assert.That(replaced["Description"]).IsNull();
        await Assert.That(Strings(replaced["Labels"])).IsEquivalentTo(new[] { "z" });
        await Assert.That((replaced["Values"] as JsonObject)?.Select(pair => pair.Key).ToList() ?? []).IsEquivalentTo(new[] { "a" });
        await Assert.That(replaced["Values"]?["a"]?.GetValue<double>()).IsEqualTo(4d);

        var created = (await Sform(InProject("story", "set", missing, "--replace", "-c", "a=1"))).ShouldSucceed().JsonNode();
        await Assert.That(created["AnnotationKey"]!.GetValue<string>()).IsEqualTo(missing);
        await Assert.That(created["Values"]?["a"]?.GetValue<double>()).IsEqualTo(1d);
        (await Sform(InProject("story", "get", missing))).ShouldSucceed();

        var history = (await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode();
        await Assert.That(Actions(history)).IsEquivalentTo(new[] { "Created", "Updated", "Updated" });
    }

    // The strings of a JSON array (Labels); empty for a missing one.
    private static IReadOnlyList<string> Strings(JsonNode? array)
    {
        return (array as JsonArray)?.Select(item => item!.GetValue<string>()).ToList() ?? [];
    }

    // The actions of the entries of story history --json.
    private static IReadOnlyList<string> Actions(JsonNode history)
    {
        return (history["Entries"] as JsonArray)?.Select(entry => entry!["Action"]!.GetValue<string>()).ToList() ?? [];
    }
}
