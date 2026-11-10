using System.Globalization;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Tests.Annotations;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// Versions, Diff, Composition and Edit: sform story config versions, diff, composition, edit and restore (of a version).
[Area(Area.Configurations)]
public sealed class ConfigHistoryTests : CliTest
{
    // A type schema of rst in a view: a must be a number.
    private const string ANumberSchema = """{ "type": "object", "properties": { "a": { "type": "number" } } }""";

    // CFG-HIS-01: config versions lists the versions newest first.
    // An Editor writes three versions of <rst> (a=1, a=2, a=3); as a Reader, config versions <rst> prints "Versions of the
    // configuration for '<rst>'" and a table whose rows are the three versions, the newest first, with their times and
    // their author: the same account in each row (its name is the provider's, so it is not compared with a value), never
    // "unknown", "system" or a machine.
    [Test]
    [TestCase(428)]
    [UserRole(Role.Reader)]
    [Covers("story config versions", "arg0")]
    public async Task Config_Versions_Listed()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var written = await WriteThreeVersionsAsync(editor, key);

        var listed = (await Sform(InProject("story", "config", "versions", key)))
            .ShouldSucceed()
            .ShouldContain($"Versions of the configuration for '{key}'")
            .ShouldMatch(ConfigOutput.DatePattern());
        await Assert.That(Join(listed.VersionRows(ConfigOutput.ConfigurationVersions))).IsEqualTo(Join(written.Reverse()));

        var authors = ConfigOutput.VersionAuthors(listed, ConfigOutput.ConfigurationVersions);
        await Assert.That(authors.Count).IsEqualTo(written.Length).Because(listed.PlainStdOut);
        await Assert.That(authors.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1).Because(listed.PlainStdOut);
        await Assert.That(authors[0] is "unknown" or "system" || authors[0].StartsWith("machine:", StringComparison.Ordinal))
            .IsFalse()
            .Because(listed.PlainStdOut);
    }

    // CFG-HIS-02: config diff compares the latest with its previous version, or two given versions.
    // An Editor writes three versions of <rst> (a=1, a=2, a=3); as a Reader, config diff <rst> shows 3 against 2 (a
    // deleted line "a": 2 and an added line "a": 3, nothing of version 1), config diff <rst> 3 1 shows 3 against 1, and
    // config diff <rst> 2 shows 2 against 1.
    [Test]
    [TestCase(430)]
    [UserRole(Role.Reader)]
    [Covers("story config diff", "arg0", "arg1", "arg2")]
    public async Task Config_Diff_Versions()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var written = await WriteThreeVersionsAsync(editor, key);

        var latest = (await Sform(InProject("story", "config", "diff", key))).ShouldSucceed();
        await AssertDiffAsync(latest, from: 2, to: 3, absent: 1);

        var custom = (await Sform(InProject("story", "config", "diff", key, Text(written[2]), Text(written[0])))).ShouldSucceed();
        await AssertDiffAsync(custom, from: 1, to: 3, absent: 2);

        var previous = (await Sform(InProject("story", "config", "diff", key, Text(written[1])))).ShouldSucceed();
        await AssertDiffAsync(previous, from: 1, to: 2, absent: 3);
    }

    // CFG-HIS-03: diff --view-to-compare compares with another view.
    // An Editor stores a=1 on <rst> in the view of the run (A) and a=2 on <rst> in a generated view (B); as a Reader,
    // config diff <rst> -v A --view-to-compare B shows A as from and B as to: a deleted line "a": 1 and an added "a": 2.
    [Test]
    [TestCase(432)]
    [UserRole(Role.Reader)]
    [Covers("story config diff", "--view-to-compare")]
    public async Task Config_DiffView_ComparesViews()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var other = CliData.ViewName();
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed();
        (await editor.Sform(ConfigOutput.In(ProjectKey, other, "story", "set", key))).ShouldSucceed();
        (await editor.Sform(ConfigOutput.In(ProjectKey, other, "story", "config", "set", key, "-x", "a=2"))).ShouldSucceed();

        var diff = (await Sform(InProject("story", "config", "diff", key, "--view-to-compare", other))).ShouldSucceed();
        await Assert.That(ConfigOutput.HasDiffLine(diff, '-', "\"a\": 1")).IsTrue().Because(diff.PlainStdOut);
        await Assert.That(ConfigOutput.HasDiffLine(diff, '+', "\"a\": 2")).IsTrue().Because(diff.PlainStdOut);
    }

    // CFG-HIS-04: config composition shows the merged documents and the step that set each line; -e saves it.
    // An Editor, in a generated view (no other template there), stores a template of unt (tpl), a configuration of <rst>
    // (inherited) and one of its unit <unt> (own). As a Reader, config composition <unt> prints "Composition of the
    // configuration for '<unt>'", the steps (the template as "template Unit") and "Effective configuration, by step";
    // -e comp.json and --export comp2.json write the composition: the three values in the content, the sources
    // configuration:<rst>, template:unt and configuration:<unt>, the steps in merge order (the parent, then the template,
    // then the own configuration), and the origin of each value pointing at the step of its source.
    [Test]
    [TestCase(433)]
    [UserRole(Role.Reader)]
    [Covers("story config composition", "arg0", "-e", "--export")]
    public async Task Config_Composition_ShowsSteps()
    {
        var editor = await ArrangeAsEditorAsync();
        var view = CliData.ViewName();
        var parent = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(parent);
        var template = CliData.Value();
        var inherited = CliData.Value();
        var own = CliData.Value();
        (await editor.Sform(At("story", "set", parent))).ShouldSucceed();
        (await editor.Sform(At("story", "set", unit))).ShouldSucceed();
        (await editor.Sform(At("story", "template", "set", "unt", "-x", $"tpl={template}"))).ShouldSucceed();
        (await editor.Sform(At("story", "config", "set", parent, "-x", $"inherited={inherited}"))).ShouldSucceed();
        (await editor.Sform(At("story", "config", "set", unit, "-x", $"own={own}"))).ShouldSucceed();

        (await Sform(At("story", "config", "composition", unit)))
            .ShouldSucceed()
            .ShouldContain($"Composition of the configuration for '{unit}'")
            .ShouldContain("template Unit")
            .ShouldContain("Effective configuration, by step")
            .ShouldContain(own)
            .ShouldContain(inherited)
            .ShouldContain(template);

        (await Sform(At("story", "config", "composition", unit, "-e", "comp.json"))).ShouldSucceed();
        (await Sform(At("story", "config", "composition", unit, "--export", "comp2.json"))).ShouldSucceed();
        foreach (var file in new[] { "comp.json", "comp2.json" })
        {
            var composition = JsonNode.Parse(await ReadFileAsync(file))!;
            await Assert.That(composition["AnnotationKey"]?.GetValue<string>()).IsEqualTo(unit);
            await Assert.That(composition["Content"]?["tpl"]?.GetValue<string>()).IsEqualTo(template);
            await Assert.That(composition["Content"]?["inherited"]?.GetValue<string>()).IsEqualTo(inherited);
            await Assert.That(composition["Content"]?["own"]?.GetValue<string>()).IsEqualTo(own);

            var templateSource = "template:unt";
            var steps = composition["Steps"]!.AsArray()
                .Select(step => (Order: step!["Order"]!.GetValue<int>(), Source: step["SourceId"]!.GetValue<string>()))
                .OrderBy(step => step.Order)
                .Select(step => step.Source)
                .ToList();
            await Assert.That(string.Join(" | ", steps)).IsEqualTo($"configuration:{parent} | {templateSource} | configuration:{unit}");
            await Assert.That(SourceOf(composition, "/tpl")).IsEqualTo(templateSource);
            await Assert.That(SourceOf(composition, "/inherited")).IsEqualTo($"configuration:{parent}");
            await Assert.That(SourceOf(composition, "/own")).IsEqualTo($"configuration:{unit}");
            var kinds = composition["Sources"]!.AsArray().ToDictionary(source => source!["Id"]!.GetValue<string>(), source => source!["Kind"]!.GetValue<string>());
            await Assert.That(kinds.GetValueOrDefault(templateSource)).IsEqualTo("Template");
            await Assert.That(kinds.GetValueOrDefault($"configuration:{unit}")).IsEqualTo("Configuration");
        }

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // CFG-HIS-05: config edit -y stores the editor's change as a new version.
    // As an Editor, with a=1 stored on <rst>, the fake editor sets a to 5: config edit <rst> -y says "Configuration for
    // '<rst>' has been saved (version N)." and N is a new version with a=5; then it sets a to 6: config edit <rst> --yes
    // gives the editor the stored a=5 and saves a=6 as the next version.
    [Test]
    [TestCase(434)]
    [UserRole(Role.Editor)]
    [Covers("story config edit", "arg0", "-y", "--yes")]
    public async Task Config_Edit_StoresChange()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        var before = (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        var editor = await UseEditorAsync();

        var five = editor.With(EditorChange.Merge("""{ "a": 5 }"""));
        var first = (await Sform(five, InProject("story", "config", "edit", key, "-y")))
            .ShouldSucceed()
            .ShouldContain($"Configuration for '{key}' has been saved (version ");
        var fifth = ConfigOutput.SavedVersion(first);
        await Assert.That(fifth).IsGreaterThan(before);
        await Assert.That(editor.ReadLog(five).Single().OriginalJson()!["a"]!.GetValue<long>()).IsEqualTo(1L);
        await AssertStoredAsync(InProject("story", "config", key), fifth, 5);

        var six = editor.With(EditorChange.Merge("""{ "a": 6 }"""));
        var second = (await Sform(six, InProject("story", "config", "edit", key, "--yes"))).ShouldSucceed();
        var sixth = ConfigOutput.SavedVersion(second);
        await Assert.That(sixth).IsGreaterThan(fifth);
        await Assert.That(editor.ReadLog(six).Single().OriginalJson()!["a"]!.GetValue<long>()).IsEqualTo(5L);
        await AssertStoredAsync(InProject("story", "config", key), sixth, 6);
    }

    // CFG-HIS-06: edit without a change stores nothing; a schema violation needs -f.
    // As an Editor, in a generated view with a type schema of rst that requires a to be a number and a=1 stored on
    // <rst>: the fake editor leaves the file as it is, and config edit <rst> -y says "No changes detected." and ends with
    // 42; then it writes a text into a: config edit <rst> -y ends with 2 and the violation ("<rst> (<view>)"), and the
    // version stays; config edit <rst> -y -f stores it, and config edit <rst> --yes --force stores another breaking value,
    // a new version each.
    [Test]
    [TestCase(435)]
    [UserRole(Role.Editor)]
    [Covers("story config edit", "-f", "--force")]
    public async Task Config_Edit_NoChangeOrForce()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        await WriteFileAsync("a-number.json", ANumberSchema);
        (await Sform(At("story", "schema", "type", "set", "rst", "-i", "a-number.json"))).ShouldSucceed();
        (await Sform(At("story", "set", key))).ShouldSucceed();
        var stored = (await Sform(At("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        var editor = await UseEditorAsync();

        (await Sform(editor.With(EditorChange.None), At("story", "config", "edit", key, "-y")))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain("No changes detected.");
        await AssertStoredAsync(At("story", "config", key), stored, 1);

        (await Sform(editor.With(EditorChange.Merge("""{ "a": "text" }""")), At("story", "config", "edit", key, "-y")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"{key} ({view})");
        await AssertStoredAsync(At("story", "config", key), stored, 1);

        var forced = ConfigOutput.SavedVersion((await Sform(editor.With(EditorChange.Merge("""{ "a": "text" }""")), At("story", "config", "edit", key, "-y", "-f"))).ShouldSucceed());
        await Assert.That(forced).IsGreaterThan(stored);
        var again = ConfigOutput.SavedVersion((await Sform(editor.With(EditorChange.Merge("""{ "a": "other" }""")), At("story", "config", "edit", key, "--yes", "--force"))).ShouldSucceed());
        await Assert.That(again).IsGreaterThan(forced);

        var current = (await Sform(At("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(current["Version"]?.GetValue<long>()).IsEqualTo(again);
        await Assert.That(current["Content"]?["a"]?.GetValue<string>()).IsEqualTo("other");

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    // CFG-HIS-07: config restore stores the content of an earlier version as a new version.
    // As an Editor, with three versions of <rst> (a=1, a=2, a=3): config restore <rst> --version 1 says "Restored v1 of the
    // configuration for '<rst>' as v4." (0); config <rst> then has a=1 in v4, config versions <rst> lists the four versions
    // with v4 restored from v1 (its Restored from cell, "—" for v3), and the newest entry of story history <rst> is "Config
    // restored" with "config v4 from v1". config restore <rst> --version 4 says "v4 is already the current version of the
    // configuration for '<rst>'." and ends with 42, and v4 stays the current version; config restore <rst> --version 9 ends
    // with 2 and "Error code: VersionNotFound".
    [Test]
    [TestCase(485)]
    [UserRole(Role.Editor)]
    [Covers("story config restore", "arg0", "--version")]
    public async Task Config_Restore_StoresAnEarlierVersion()
    {
        var key = CliData.ResponsibilityKey();
        var written = await WriteVersionsAsync(InProject, key, "1", "2", "3");
        var restored = written[2] + 1;

        (await Sform(InProject("story", "config", "restore", key, "--version", Text(written[0]))))
            .ShouldSucceed()
            .ShouldContain($"Restored v{written[0]} of the configuration for '{key}' as v{restored}.");
        await AssertStoredAsync(InProject("story", "config", key), restored, 1);

        var listed = (await Sform(InProject("story", "config", "versions", key))).ShouldSucceed();
        await Assert.That(Join(listed.VersionRows(ConfigOutput.ConfigurationVersions))).IsEqualTo(Join([restored, .. written.Reverse()]));
        var restoredFrom = ConfigOutput.VersionRestoredFrom(listed, ConfigOutput.ConfigurationVersions);
        await Assert.That(restoredFrom.GetValueOrDefault(restored)).IsEqualTo($"v{written[0]}").Because(listed.PlainStdOut);
        await Assert.That(restoredFrom.GetValueOrDefault(written[2]) ?? string.Empty).DoesNotStartWith("v").Because(listed.PlainStdOut);

        var history = (await Sform(InProject("story", "history", key))).ShouldSucceed();
        var rows = AnnotationOutput.HistoryRows(history);
        var newest = rows.Count > 0 ? rows[0] : string.Empty;
        await Assert.That(newest).Contains("Config restored").Because(history.PlainStdOut);
        await Assert.That(newest).Contains($"config v{restored} from v{written[0]}").Because(history.PlainStdOut);

        (await Sform(InProject("story", "config", "restore", key, "--version", Text(restored))))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain($"v{restored} is already the current version of the configuration for '{key}'.");
        await AssertStoredAsync(InProject("story", "config", key), restored, 1);

        (await Sform(InProject("story", "config", "restore", key, "--version", Text(restored + 5))))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Error code: VersionNotFound");
    }

    // CFG-HIS-08: config restore brings back a deleted configuration.
    // As an Editor, with two versions of <rst> (a=1, a=2), deleted by config delete <rst>: config restore <rst> --version 2
    // says "Restored v2 of the configuration for '<rst>' as v3." (0); config <rst> then has a=2 in v3, and config versions
    // <rst> lists the three versions.
    [Test]
    [TestCase(486)]
    [UserRole(Role.Editor)]
    [Covers("story config restore", "arg0", "--version")]
    public async Task Config_Restore_AfterDelete()
    {
        var key = CliData.ResponsibilityKey();
        var written = await WriteVersionsAsync(InProject, key, "1", "2");
        (await Sform(InProject("story", "config", "delete", key)))
            .ShouldSucceed()
            .ShouldContain($"The configuration for '{key}' has been deleted.");
        var restored = written[1] + 1;

        (await Sform(InProject("story", "config", "restore", key, "--version", Text(written[1]))))
            .ShouldSucceed()
            .ShouldContain($"Restored v{written[1]} of the configuration for '{key}' as v{restored}.");
        await AssertStoredAsync(InProject("story", "config", key), restored, 2);

        var listed = (await Sform(InProject("story", "config", "versions", key))).ShouldSucceed();
        await Assert.That(Join(listed.VersionRows(ConfigOutput.ConfigurationVersions))).IsEqualTo(Join([restored, .. written.Reverse()]));
    }

    // CFG-HIS-09: config restore of a version that violates the schema fails, and -f or --force stores it.
    // As an Editor, in a generated view: versions a=text (v1) and a=1 (v2) of <rst>, then a type schema of rst that requires
    // a to be a number. config restore <rst> --version 1 ends with 2 and the violation ("<rst> (<view>)"), and v2 stays the
    // current version; config restore <rst> --version 1 -f says "Restored v1 of the configuration for '<rst>' as v3."; after
    // config set <rst> -x a=2 (v4), config restore <rst> --version 1 --force says "… as v5.", and the configuration has
    // a=text in v5.
    [Test]
    [TestCase(487)]
    [UserRole(Role.Editor)]
    [Covers("story config restore", "-f", "--force")]
    public async Task Config_Restore_SchemaViolation_Force()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var written = await WriteVersionsAsync(At, key, "text", "1");
        await WriteFileAsync("a-number.json", ANumberSchema);
        (await Sform(At("story", "schema", "type", "set", "rst", "-i", "a-number.json"))).ShouldSucceed();

        (await Sform(At("story", "config", "restore", key, "--version", Text(written[0]))))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"{key} ({view})");
        await AssertStoredAsync(At("story", "config", key), written[1], 1);

        (await Sform(At("story", "config", "restore", key, "--version", Text(written[0]), "-f")))
            .ShouldSucceed()
            .ShouldContain($"Restored v{written[0]} of the configuration for '{key}' as v{written[1] + 1}.");

        var set = (await Sform(At("story", "config", "set", key, "-x", "a=2"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        (await Sform(At("story", "config", "restore", key, "--version", Text(written[0]), "--force")))
            .ShouldSucceed()
            .ShouldContain($"Restored v{written[0]} of the configuration for '{key}' as v{set + 1}.");

        var current = (await Sform(At("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(current["Version"]?.GetValue<long>()).IsEqualTo(set + 1);
        await Assert.That(current["Content"]?["a"]?.GetValue<string>()).IsEqualTo("text");

        string[] At(params string[] arguments) => ConfigOutput.In(ProjectKey, view, arguments);
    }

    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);

    private static string Join(IEnumerable<long> versions) => string.Join(", ", versions);

    // The step's source of the value at the pointer: Origins (Path, Step) and Steps (Order, SourceId) of a composition.
    private static string? SourceOf(JsonNode composition, string pointer)
    {
        var step = composition["Origins"]?.AsArray()
            .FirstOrDefault(origin => origin?["Path"]?.GetValue<string>() == pointer)?["Step"]?.GetValue<int>();
        return composition["Steps"]?.AsArray()
            .FirstOrDefault(candidate => candidate?["Order"]?.GetValue<int>() == step)?["SourceId"]?.GetValue<string>();
    }

    // A diff of single-property documents { "a": n }: the from value deleted, the to value added, the third value absent.
    private static async Task AssertDiffAsync(SformResult diff, int from, int to, int absent)
    {
        await Assert.That(ConfigOutput.HasDiffLine(diff, '-', $"\"a\": {from}")).IsTrue().Because(diff.PlainStdOut);
        await Assert.That(ConfigOutput.HasDiffLine(diff, '+', $"\"a\": {to}")).IsTrue().Because(diff.PlainStdOut);
        await Assert.That(diff.PlainStdOut).DoesNotContain($"\"a\": {absent}");
    }

    // Three versions of a new configuration of the key (a=1, a=2, a=3) written by the actor; their versions in order.
    private async Task<long[]> WriteThreeVersionsAsync(SformActor editor, string key)
    {
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        var versions = new long[3];
        for (var index = 0; index < versions.Length; index++)
        {
            var set = (await editor.Sform(InProject("story", "config", "set", key, "-x", $"a={index + 1}"))).ShouldSucceed();
            versions[index] = set.JsonNode()["Version"]!.GetValue<long>();
        }

        return versions;
    }

    // A new annotation of the key and a version of its configuration per value (a=<value>), written by the leased user with
    // the context of at (InProject, or another view); their versions in order.
    private async Task<long[]> WriteVersionsAsync(Func<string[], string[]> at, string key, params string[] values)
    {
        (await Sform(at(["story", "set", key]))).ShouldSucceed();
        var versions = new long[values.Length];
        for (var index = 0; index < versions.Length; index++)
        {
            var set = (await Sform(at(["story", "config", "set", key, "-x", $"a={values[index]}"]))).ShouldSucceed();
            versions[index] = set.JsonNode()["Version"]!.GetValue<long>();
        }

        return versions;
    }

    // The model of the read has the version and the number a.
    private async Task AssertStoredAsync(string[] read, long version, long a)
    {
        var model = (await Sform(read)).ShouldSucceed().JsonNode();
        await Assert.That(model["Version"]?.GetValue<long>()).IsEqualTo(version);
        await Assert.That(model["Content"]?["a"]?.GetValue<long>()).IsEqualTo(a);
    }
}
