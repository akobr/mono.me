using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Tests.Annotations;
using _42.Platform.Cli.AutomationTests.Tests.KeyValues;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// Project Settings: sform project settings shows the settings of the context project (the names, colors and icons of
// its seven annotation types, its default time zone and its date format), and set, edit and reset change them, which
// needs the Administrator role (the API refuses machines and the other roles with 403). The settings change only the
// console output for people (in a terminal): never what sform accepts, writes to files or prints when its output is
// redirected. Every case that changes settings does it in a disposable project of its own (mode P): the target project
// and the role projects never get settings, as the parsers of dates and type names of the other cases expect the
// built-in output; a Reader, a Contributor and a machine are refused there too, so a Debug API that skips the role checks
// changes nothing but a disposable project.
[Area(Area.Account)]
public sealed partial class ProjectSettingsTests : CliTest
{
    private const string Company = "Company";

    // The settings of a project that never set them (version 0).
    private const string DefaultTimeZoneLine = "Default time zone: UTC (default)";
    private const string DefaultDateFormatLine = "Date format: yyyy-mm-dd (default)";

    // How long the audit trail may take to list the events of the writes.
    private static readonly TimeSpan AuditWait = TimeSpan.FromSeconds(30);

    // ACC-PRS-01: project settings shows a Reader the settings of the project as a table and with -f json as the API
    // answers them; an unknown format fails with exit 2.
    // As exactly a Reader (in <reader project> of Target:RoleProjects, or a Reader of the target project): project
    // settings -p <org>.<project> --format json prints the settings with Types (an object) and Version <n>; project settings
    // -p <org>.<project> and the same with -f text print "# Project settings of <org>.<project>", the header Code, Type,
    // Name, Plural, Color, Icon and a row per type code with its built-in name, then "Version: <n>" and "Change them with
    // sform project settings set, edit and reset." (at version 0, as the target's projects are, both defaults marked
    // "(default)" and "Modified: never"); -f yaml ends with 2 and "Unknown format 'yaml'; use text or json." Nothing is
    // written.
    [Test]
    [TestCase(499)]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("project settings", "-f", "--format", "--format=text", "--format=json")]
    public async Task Settings_Reader_ReadsTableAndJson()
    {
        var json = (await Sform("project", "settings", "-p", ProjectKey, "--format", "json")).ShouldSucceed().JsonNode();
        var version = ProjectSettingsOutput.VersionOf(json);
        await Assert.That(json["Types"] is JsonObject).IsTrue().Because(json.ToJsonString());

        foreach (var arguments in new[] { new[] { "project", "settings", "-p", ProjectKey }, new[] { "project", "settings", "-p", ProjectKey, "-f", "text" } })
        {
            var table = (await Sform(arguments))
                .ShouldSucceed()
                .ShouldHaveLine($"# Project settings of {ProjectKey}")
                .ShouldHaveLine($"Version: {version.ToString(CultureInfo.InvariantCulture)}")
                .ShouldHaveLine(ProjectSettingsOutput.ChangeHint);
            await Assert.That(ProjectSettingsOutput.HasHeader(table)).IsTrue().Because(table.PlainStdOut);

            foreach (var (code, name) in ProjectSettingsOutput.BuiltInTypes)
            {
                var row = ProjectSettingsOutput.TypeRow(table, code) ?? throw table.Fail($"printed no row of the type {code}");
                await Assert.That(row[1]).IsEqualTo(name);
            }

            if (version == 0)
            {
                table.ShouldHaveLine(DefaultTimeZoneLine).ShouldHaveLine(DefaultDateFormatLine).ShouldHaveLine("Modified: never");
            }
        }

        (await Sform("project", "settings", "-p", ProjectKey, "-f", "yaml"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Unknown format 'yaml'; use text or json.");
    }

    // ACC-PRS-02: project settings set merges the appearance of one type, the time zone and the date format into the
    // settings, and --clear removes a type's appearance.
    // In a disposable project (version 0): set --type sbt --label Company --plural Companies --color emerald --icon
    // building-2 shows the changes and saves version 1; the same icon by --type subject ends with 42 ("did not change; they
    // stay at version 1."); --type "unit of execution" --label Run --time-zone europe/prague --date-format DD.MM.YYYY saves
    // version 2. -f json then has sbt (Company, Companies, emerald, i-lucide-building-2), uxe (Run), Europe/Prague and
    // dd.mm.yyyy in the catalog's spelling, Version 2 and Modified; the table shows the rows, uxe with the derived plural
    // Runs and the built-in color and icon. --type SBT --clear --label Firm saves version 3 with the label Firm only, --type
    // sbt --clear version 4 without sbt (uxe, the time zone and the date format stay), and story kv audit --operation
    // projectsettingschange lists four events, one per saved version.
    [Test]
    [TestCase(500)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("project settings set", "--type", "--label", "--plural", "--color", "--icon", "--clear", "--time-zone", "--date-format")]
    public async Task SettingsSet_MergesTypeTimeZoneAndDateFormat()
    {
        await CreateDisposableProjectAsync();

        (await Sform(ProjectSettings("set", "--type", "sbt", "--label", Company, "--plural", "Companies", "--color", "emerald", "--icon", "building-2")))
            .ShouldSucceed()
            .ShouldHaveLine("# Changes")
            .ShouldContain(ProjectSettingsOutput.Saved(1));

        (await Sform(ProjectSettings("set", "--type", "subject", "--icon", "i-lucide-building-2")))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain(ProjectSettingsOutput.Unchanged(1));

        (await Sform(ProjectSettings("set", "--type", "unit of execution", "--label", "Run", "--time-zone", "europe/prague", "--date-format", "DD.MM.YYYY")))
            .ShouldSucceed()
            .ShouldContain(ProjectSettingsOutput.Saved(2));

        var json = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.VersionOf(json)).IsEqualTo(2L);
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "sbt", "Label")).IsEqualTo(Company);
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "sbt", "Plural")).IsEqualTo("Companies");
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "sbt", "Color")).IsEqualTo("emerald");
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "sbt", "Icon")).IsEqualTo("i-lucide-building-2");
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "uxe", "Label")).IsEqualTo("Run");
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "uxe", "Plural")).IsNull();
        await Assert.That(ProjectSettingsOutput.Value(json, "DefaultTimeZone")).IsEqualTo("Europe/Prague");
        await Assert.That(ProjectSettingsOutput.Value(json, "DateFormat")).IsEqualTo("dd.mm.yyyy");
        await Assert.That(json["Modified"]?["At"]).IsNotNull();

        var table = (await Sform(ProjectSettings()))
            .ShouldSucceed()
            .ShouldHaveLine("Default time zone: Europe/Prague")
            .ShouldHaveLine("Date format: dd.mm.yyyy")
            .ShouldHaveLine("Version: 2")
            .ShouldMatch(ModifiedLine());
        await Assert.That(string.Join(" | ", ProjectSettingsOutput.TypeRow(table, "sbt") ?? [])).IsEqualTo("sbt | Subject | Company | Companies | emerald | i-lucide-building-2");
        await Assert.That(string.Join(" | ", ProjectSettingsOutput.TypeRow(table, "uxe") ?? [])).IsEqualTo("uxe | Unit of execution | Run | Runs | orange | i-lucide-cpu");

        (await Sform(ProjectSettings("set", "--type", "SBT", "--clear", "--label", "Firm"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(3));
        var firm = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.TypeOf(firm, "sbt")?.ToJsonString()).IsEqualTo(new JsonObject { ["Label"] = "Firm" }.ToJsonString());

        (await Sform(ProjectSettings("set", "--type", "sbt", "--clear"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(4));
        var cleared = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.TypeCodes(cleared)).IsEquivalentTo(new[] { "uxe" });
        await Assert.That(ProjectSettingsOutput.Value(cleared, "DefaultTimeZone")).IsEqualTo("Europe/Prague");
        await Assert.That(ProjectSettingsOutput.Value(cleared, "DateFormat")).IsEqualTo("dd.mm.yyyy");

        var events = await EventuallyAsync(
            async () => KeyValuesSupportB.AuditRows(
                (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", "projectsettingschange"))).ShouldSucceed()),
            rows => rows.Count >= 4,
            AuditWait);
        await Assert.That(events.Select(row => row.Operation).Distinct().ToArray()).IsEquivalentTo(new[] { "ProjectSettingsChange" });

        // one event per saved version, whatever the order of the rows: the detail starts with "v<old>→v<new>:"
        var transitions = events
            .Select(row => VersionChange().Match(row.Line) is { Success: true } match ? $"{match.Groups[1].Value}-{match.Groups[2].Value}" : row.Line)
            .ToArray();
        await Assert.That(transitions).IsEquivalentTo(new[] { "0-1", "1-2", "2-3", "3-4" });
    }

    // ACC-PRS-03: project settings -e exports the settings in the file shape, and set -i merges a settings file into them
    // or replaces them with --replace.
    // In a disposable project with exe (Job, emerald) and America/Phoenix (version 2): project settings -e exported.json
    // and --export again.json print the table and write { "Format": "2s.project-settings.v1", "Types": { "exe": … },
    // "DefaultTimeZone": "America/Phoenix" } without Version and Modified. set -i import.json (rst Solution with an icon and
    // mm/dd/yyyy) merges it (version 3: exe, rst, the time zone, the date format); set --import import.json --replace
    // replaces the settings with it (version 4: rst and mm/dd/yyyy only); set -i exported.json --replace gives back the
    // exported settings (version 5). set -i bad.json (the unknown member Colour) ends with 2, "The file '<path>' holds no
    // valid project settings:" and "$.Types.exe.Colour: 'Colour' is not a member; …", before any write.
    [Test]
    [TestCase(501)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("project settings", "-e", "--export")]
    [Covers("project settings set", "-i", "--import", "--replace")]
    public async Task Settings_ExportImportAndReplace()
    {
        await CreateDisposableProjectAsync();
        (await Sform(ProjectSettings("set", "--type", "exe", "--label", "Job", "--color", "emerald"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(1));
        (await Sform(ProjectSettings("set", "--time-zone", "America/Phoenix"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(2));

        var expected = new JsonObject
        {
            ["Format"] = ProjectSettingsOutput.FileFormat,
            ["Types"] = new JsonObject { ["exe"] = new JsonObject { ["Label"] = "Job", ["Color"] = "emerald" } },
            ["DefaultTimeZone"] = "America/Phoenix",
        };
        foreach (var (option, file) in new[] { ("-e", "exported.json"), ("--export", "again.json") })
        {
            (await Sform(ProjectSettings(option, file))).ShouldSucceed().ShouldHaveLine($"# Project settings of {ProjectKey}");
            var exported = JsonNode.Parse(await ReadFileAsync(file))!;
            await Assert.That(JsonNode.DeepEquals(exported, expected)).IsTrue().Because(exported.ToJsonString());
        }

        await WriteFileAsync("import.json", """
            {
              "Format": "2s.project-settings.v1",
              "Types": { "rst": { "Label": "Solution", "Icon": "i-lucide-briefcase" } },
              "DateFormat": "mm/dd/yyyy"
            }
            """);
        await WriteFileAsync("bad.json", """{ "Types": { "exe": { "Colour": "red" } } }""");

        (await Sform(ProjectSettings("set", "-i", "import.json"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(3));
        var merged = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.TypeCodes(merged)).IsEquivalentTo(new[] { "rst", "exe" });
        await Assert.That(ProjectSettingsOutput.TypeValue(merged, "rst", "Label")).IsEqualTo("Solution");
        await Assert.That(ProjectSettingsOutput.TypeValue(merged, "rst", "Icon")).IsEqualTo("i-lucide-briefcase");
        await Assert.That(ProjectSettingsOutput.TypeValue(merged, "exe", "Label")).IsEqualTo("Job");
        await Assert.That(ProjectSettingsOutput.Value(merged, "DefaultTimeZone")).IsEqualTo("America/Phoenix");
        await Assert.That(ProjectSettingsOutput.Value(merged, "DateFormat")).IsEqualTo("mm/dd/yyyy");

        (await Sform(ProjectSettings("set", "--import", "import.json", "--replace"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(4));
        var replaced = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.TypeCodes(replaced)).IsEquivalentTo(new[] { "rst" });
        await Assert.That(ProjectSettingsOutput.Value(replaced, "DefaultTimeZone")).IsNull();
        await Assert.That(ProjectSettingsOutput.Value(replaced, "DateFormat")).IsEqualTo("mm/dd/yyyy");

        (await Sform(ProjectSettings("set", "-i", "exported.json", "--replace"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(5));
        var restored = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.TypeCodes(restored)).IsEquivalentTo(new[] { "exe" });
        await Assert.That(ProjectSettingsOutput.TypeValue(restored, "exe", "Color")).IsEqualTo("emerald");
        await Assert.That(ProjectSettingsOutput.Value(restored, "DefaultTimeZone")).IsEqualTo("America/Phoenix");
        await Assert.That(ProjectSettingsOutput.Value(restored, "DateFormat")).IsNull();

        (await Sform(ProjectSettings("set", "-i", "bad.json")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("holds no valid project settings:")
            .ShouldContain("$.Types.exe.Colour: 'Colour' is not a member;")
            .ShouldNotContain("Saved the project settings");
        await Assert.That(ProjectSettingsOutput.VersionOf(await ReadSettingsAsync())).IsEqualTo(5L);
    }

    // ACC-PRS-04: project settings edit saves the confirmed document of the editor; an unchanged edit saves nothing, and an
    // invalid or outdated one is kept in a file.
    // In a disposable project, in a terminal with the fake editor: edit opens the file shape (Format, Types {}, no
    // Version); the editor sets exe (Job, emerald) and d mmm yyyy, Yes saves version 1. An edit that leaves the file prints
    // "No changes detected." (42). An edit to the color chartreuse prints the problem, No to the re-open question keeps the
    // edit ("Your edit is in <file>. Fix it, then save it with: sform project settings set -p <org>.<project> -i "<file>"
    // --replace --expected-version 1", 2). An edit to dd/mm/yyyy, answered after a second user, an Administrator of the
    // project, saved mm/dd/yyyy (version 2), ends with "The project settings changed meanwhile; they are at version 2." and
    // keeps the edit with the advice to merge it into the current settings ("It started from an older version: …", 2);
    // set -i <file> --replace --expected-version 2 saves it as version 3.
    [Test]
    [TestCase(502)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("project settings edit")]
    public async Task SettingsEdit_SavesConfirmedDocument()
    {
        var project = await CreateDisposableProjectAsync();
        var editor = await UseEditorAsync();

        var change = editor.With(EditorChange.Merge(new JsonObject
        {
            ["Types"] = new JsonObject { ["exe"] = new JsonObject { ["Label"] = "Job", ["Color"] = "emerald" } },
            ["DateFormat"] = "d mmm yyyy",
        }));
        await using (var terminal = await StartSformInTerminal(change, ProjectSettings("edit")))
        {
            await terminal.ExpectAsync("Changes");
            await AnnotationEditorTests.AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync(ProjectSettingsOutput.Saved(1));
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        var opened = editor.ReadLog(change).Single().OriginalJson()!;
        await Assert.That(ProjectSettingsOutput.Value(opened, "Format")).IsEqualTo(ProjectSettingsOutput.FileFormat);
        await Assert.That(opened["Types"] is JsonObject { Count: 0 }).IsTrue().Because(opened.ToJsonString());
        await Assert.That(opened["Version"]).IsNull();
        var saved = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.TypeValue(saved, "exe", "Label")).IsEqualTo("Job");
        await Assert.That(ProjectSettingsOutput.TypeValue(saved, "exe", "Color")).IsEqualTo("emerald");
        await Assert.That(ProjectSettingsOutput.Value(saved, "DateFormat")).IsEqualTo("d mmm yyyy");

        var unchanged = editor.With(EditorChange.None);
        await using (var terminal = await StartSformInTerminal(unchanged, ProjectSettings("edit")))
        {
            await terminal.ExpectAsync("No changes detected.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.NoWorkNeeded).ShouldNotContain(AnnotationEditorTests.SaveQuestion);
        }

        var refusedFolder = TemporaryFolder("refused");
        var invalid = editor.With(EditorChange.Merge("""{ "Types": { "exe": { "Color": "chartreuse" } } }"""), InTemporaryFolder(refusedFolder));
        await using (var terminal = await StartSformInTerminal(invalid, ProjectSettings("edit")))
        {
            await terminal.ExpectAsync("$.Types.exe.Color: 'chartreuse' is not a color of the catalog");
            await terminal.ExpectAsync(AnnotationEditorTests.ReopenQuestion);
            await terminal.SendLineAsync("n");
            await terminal.ExpectAsync("Your edit is in");
            await terminal.ExpectAsync($"Fix it, then save it with: sform project settings set -p {project} -i");
            await terminal.ExpectAsync("--replace --expected-version 1");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.WrongInput);
        }

        var refused = JsonNode.Parse(await FileSystem.File.ReadAllTextAsync(KeptEdit(refusedFolder, project)))!;
        await Assert.That(ProjectSettingsOutput.TypeValue(refused, "exe", "Color")).IsEqualTo("chartreuse");
        await Assert.That(ProjectSettingsOutput.VersionOf(await ReadSettingsAsync())).IsEqualTo(1L);

        // the second user is signed in before the edit waits at its question
        var administrator = await AsGrantedUserAsync(PlatformRoles.Administrator);
        await administrator.Sform(ProjectSettings()).ShouldSucceed();

        var staleFolder = TemporaryFolder("stale");
        var stale = editor.With(EditorChange.Merge("""{ "DateFormat": "dd/mm/yyyy" }"""), InTemporaryFolder(staleFolder));
        await using (var terminal = await StartSformInTerminal(stale, ProjectSettings("edit")))
        {
            await terminal.ExpectAsync(AnnotationEditorTests.SaveQuestion);
            (await administrator.Sform(ProjectSettings("set", "--date-format", "mm/dd/yyyy"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(2));
            await terminal.SendLineAsync("y");
            await terminal.ExpectAsync(ProjectSettingsOutput.ChangedMeanwhile(2));
            await terminal.ExpectAsync("Your edit is in");
            await terminal.ExpectAsync("It started from an older version: read the current settings with");
            await terminal.ExpectAsync("--replace --expected-version <the version it shows>.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.WrongInput);
        }

        // the kept edit differs from version 2 only in the date format, so the merge keeps it as it is
        var kept = KeptEdit(staleFolder, project);
        await Assert.That(ProjectSettingsOutput.Value(JsonNode.Parse(await FileSystem.File.ReadAllTextAsync(kept))!, "DateFormat")).IsEqualTo("dd/mm/yyyy");
        (await Sform(ProjectSettings("set", "-i", kept, "--replace", "--expected-version", "2"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(3));
        var final = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.Value(final, "DateFormat")).IsEqualTo("dd/mm/yyyy");
        await Assert.That(ProjectSettingsOutput.TypeValue(final, "exe", "Label")).IsEqualTo("Job");
        await Assert.That(ProjectSettingsOutput.TypeValue(final, "exe", "Color")).IsEqualTo("emerald");
    }

    // ACC-PRS-05: project settings reset resets the named parts or all of them after a confirmation or with -y; on
    // redirected input without -y it ends with 43.
    // In a disposable project with exe Job, Europe/Prague and dd.mm.yyyy (version 2): reset --date-format with input
    // redirected ends with 43 and the hint on stderr, changing nothing; reset --date-format -y (version 3), --time-zone
    // --yes (4) and --types -y (5) remove one part each; reset -y then ends with 42 ("did not change; they stay at version
    // 5."). After set --type exe --label Job --date-format dd.mm.yyyy (6), reset in a terminal asks "Reset the types, the
    // default time zone and the date format of <org>.<project> to the defaults", and No prints "Nothing was reset." (40);
    // reset --types --date-format asks "Reset the types and the date format of …", and Yes saves version 7, the defaults.
    [Test]
    [TestCase(503)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("project settings reset", "--types", "--time-zone", "--date-format", "-y", "--yes")]
    public async Task SettingsReset_PartsOrAll()
    {
        var project = await CreateDisposableProjectAsync();
        (await Sform(ProjectSettings("set", "--type", "exe", "--label", "Job"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(1));
        (await Sform(ProjectSettings("set", "--time-zone", "Europe/Prague", "--date-format", "dd.mm.yyyy"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(2));

        (await Sform(ProjectSettings("reset", "--date-format")))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the confirmation of the reset: the input is redirected.", SformStream.StdErr)
            .ShouldContain("Pass -y|--yes to reset them without asking.", SformStream.StdErr);
        await Assert.That(ProjectSettingsOutput.VersionOf(await ReadSettingsAsync())).IsEqualTo(2L);

        (await Sform(ProjectSettings("reset", "--date-format", "-y"))).ShouldSucceed().ShouldHaveLine("# Changes").ShouldContain(ProjectSettingsOutput.Saved(3));
        var withoutFormat = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.Value(withoutFormat, "DateFormat")).IsNull();
        await Assert.That(ProjectSettingsOutput.Value(withoutFormat, "DefaultTimeZone")).IsEqualTo("Europe/Prague");
        await Assert.That(ProjectSettingsOutput.TypeCodes(withoutFormat)).IsEquivalentTo(new[] { "exe" });

        (await Sform(ProjectSettings("reset", "--time-zone", "--yes"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(4));
        var withoutZone = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.Value(withoutZone, "DefaultTimeZone")).IsNull();
        await Assert.That(ProjectSettingsOutput.TypeCodes(withoutZone)).IsEquivalentTo(new[] { "exe" });

        (await Sform(ProjectSettings("reset", "--types", "-y"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(5));
        await Assert.That(ProjectSettingsOutput.TypeCodes(await ReadSettingsAsync()).Count).IsEqualTo(0);

        (await Sform(ProjectSettings("reset", "-y"))).ShouldExitWith(CliExit.NoWorkNeeded).ShouldContain(ProjectSettingsOutput.Unchanged(5));

        (await Sform(ProjectSettings("set", "--type", "exe", "--label", "Job", "--date-format", "dd.mm.yyyy"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(6));
        await using (var terminal = await StartSformInTerminal(ProjectSettings("reset")))
        {
            await terminal.ExpectAsync("Changes");
            await terminal.ExpectAsync($"Reset the types, the default time zone and the date format of {project} to the defaults");
            await terminal.SendLineAsync("n");
            await terminal.ExpectAsync("Nothing was reset.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted);
        }

        await Assert.That(ProjectSettingsOutput.VersionOf(await ReadSettingsAsync())).IsEqualTo(6L);

        await using (var terminal = await StartSformInTerminal(ProjectSettings("reset", "--types", "--date-format")))
        {
            await terminal.ExpectAsync($"Reset the types and the date format of {project} to the defaults");
            await terminal.SendLineAsync("y");
            await terminal.ExpectAsync(ProjectSettingsOutput.Saved(7));
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        var reset = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.VersionOf(reset)).IsEqualTo(7L);
        await Assert.That(ProjectSettingsOutput.TypeCodes(reset).Count).IsEqualTo(0);
        await Assert.That(ProjectSettingsOutput.Value(reset, "DateFormat")).IsNull();
    }

    // ACC-PRS-06: set --dry-run shows a change without saving it, an invalid value fails with exit 2 with and without
    // --dry-run, and wrong options fail before any call.
    // In a disposable project (version 0): set --type sbt --label Company --dry-run shows the change and "Dry run: nothing was
    // saved; the project settings stay at version 0." (0); --type sbt --clear --dry-run ends with 42 ("would not change");
    // the time zone "Central Europe Standard Time" and the color chartreuse end with 2 and the rule's message with and
    // without --dry-run (the shared rules here, the API there), the date format yy.mm.dd and a label of 41 characters with
    // --dry-run too; --type solution, --label without --type, --type alone and no option at all end with 2 before any call.
    // The settings stay at version 0.
    [Test]
    [TestCase(504)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("project settings set", "--dry-run", "--type", "--label", "--color", "--clear", "--time-zone", "--date-format")]
    public async Task SettingsSet_DryRunAndInvalidValues()
    {
        await CreateDisposableProjectAsync();

        var dryRun = (await Sform(ProjectSettings("set", "--type", "sbt", "--label", Company, "--dry-run")))
            .ShouldSucceed()
            .ShouldHaveLine("# Changes")
            .ShouldContain(ProjectSettingsOutput.DryRun(0));
        await Assert.That(ProjectSettingsOutput.HasChange(dryRun, '+', $"\"Label\": \"{Company}\"")).IsTrue().Because(dryRun.PlainStdOut);

        (await Sform(ProjectSettings("set", "--type", "sbt", "--clear", "--dry-run")))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain(ProjectSettingsOutput.WouldNotChange(0));

        const string zoneProblem = "$.DefaultTimeZone: 'Central Europe Standard Time' is not an IANA time zone id of the tz database, such as Europe/Prague or UTC.";
        const string colorProblem = "$.Types.sbt.Color: 'chartreuse' is not a color of the catalog; use one of red, orange,";
        foreach (var dry in new[] { true, false })
        {
            string[] mode = dry ? ["--dry-run"] : [];
            (await Sform(ProjectSettings(["set", "--time-zone", "Central Europe Standard Time", .. mode])))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain(zoneProblem);
            (await Sform(ProjectSettings(["set", "--type", "sbt", "--color", "chartreuse", .. mode])))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain(colorProblem);
        }

        (await Sform(ProjectSettings("set", "--date-format", "yy.mm.dd", "--dry-run")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("$.DateFormat: 'yy.mm.dd' is not a date format; use one of yyyy-mm-dd, dd.mm.yyyy, dd/mm/yyyy, mm/dd/yyyy, d mmm yyyy.");
        (await Sform(ProjectSettings("set", "--type", "sbt", "--label", new string('x', 41), "--dry-run")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("$.Types.sbt.Label: The label has 41 characters; at most 40 are allowed.");

        (await Sform(ProjectSettings("set", "--type", "solution", "--label", "X")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("'solution' is no annotation type; give a type code or its built-in name: rst (Responsibility), unt (Unit), sbt (Subject), usg (Usage), cnt (Context), exe (Execution), uxe (Unit of execution).");
        (await Sform(ProjectSettings("set", "--label", "X")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("--label, --plural, --color, --icon and --clear need --type, the type they change.");
        (await Sform(ProjectSettings("set", "--type", "sbt")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("--type sbt changes nothing on its own; add --label, --plural, --color, --icon or --clear.");
        (await Sform(ProjectSettings("set")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Nothing to change: give -i|--import, --type with --label, --plural, --color, --icon or --clear, --time-zone, or --date-format.");

        var settings = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.VersionOf(settings)).IsEqualTo(0L);
        await Assert.That(ProjectSettingsOutput.TypeCodes(settings).Count).IsEqualTo(0);
    }

    // ACC-PRS-07: set --expected-version changes only that version; a Reader, a Contributor and a machine read the settings
    // and are refused a change with exit 44.
    // In a disposable project (version 0), with a -w machine of it, a second pool user granted Reader and a third granted
    // Contributor: set --date-format dd.mm.yyyy --expected-version 0 saves version 1, and --date-format mm/dd/yyyy
    // --expected-version 0 then ends with 2 and "The project settings changed meanwhile; they are at version 1." The Reader
    // and the machine read version 1; the set of the Reader, the set and reset -y of the Contributor end with 44, "No
    // Administrator access to '<org>.<project>'." and "Error code: AccessDenied", the machine's set with 44 and "Changing the
    // project settings needs a user with the Administrator role."; the settings stay at version 1 with dd.mm.yyyy.
    [Test]
    [TestCase(505)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("project settings")]
    [Covers("project settings set", "--expected-version")]
    [Covers("project settings reset")]
    public async Task SettingsSet_ExpectedVersionAndRefusals()
    {
        var project = await CreateDisposableProjectAsync();
        var machine = await CreateMachineAsync("-w");
        var reader = await AsGrantedUserAsync(PlatformRoles.Reader);
        var contributor = await AsGrantedUserAsync(PlatformRoles.Contributor);

        (await Sform(ProjectSettings("set", "--date-format", "dd.mm.yyyy", "--expected-version", "0"))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(1));
        (await Sform(ProjectSettings("set", "--date-format", "mm/dd/yyyy", "--expected-version", "0")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain(ProjectSettingsOutput.ChangedMeanwhile(1))
            .ShouldNotContain("Saved the project settings");

        var read = (await reader.Sform(ProjectSettings("--format", "json"))).ShouldSucceed().JsonNode();
        await Assert.That(ProjectSettingsOutput.VersionOf(read)).IsEqualTo(1L);
        await Assert.That(ProjectSettingsOutput.Value(read, "DateFormat")).IsEqualTo("dd.mm.yyyy");

        var refusals = new[]
        {
            await reader.Sform(ProjectSettings("set", "--date-format", "mm/dd/yyyy")),
            await contributor.Sform(ProjectSettings("set", "--date-format", "mm/dd/yyyy")),
            await contributor.Sform(ProjectSettings("reset", "-y")),
        };
        foreach (var refusal in refusals)
        {
            refusal.ShouldExitWith(CliExit.Unauthorized)
                .ShouldContain(ProjectSettingsOutput.NoAdministratorAccess(project))
                .ShouldContain("Error code: AccessDenied");
        }

        var machineRead = (await Sform(AsMachine(machine.Credentials!), "project", "settings", "--format", "json")).ShouldSucceed().JsonNode();
        await Assert.That(ProjectSettingsOutput.VersionOf(machineRead)).IsEqualTo(1L);
        (await Sform(AsMachine(machine.Credentials!), "project", "settings", "set", "--date-format", "mm/dd/yyyy"))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain(ProjectSettingsOutput.MachineRefusal)
            .ShouldContain("Error code: AccessDenied");

        var settings = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.VersionOf(settings)).IsEqualTo(1L);
        await Assert.That(ProjectSettingsOutput.Value(settings, "DateFormat")).IsEqualTo("dd.mm.yyyy");
    }

    // ACC-PRS-08: project settings set takes each color, each icon (by its catalog name or its short name) and each date
    // format of the catalog.
    // In a disposable project: one set --type <code> --color <color> --icon <icon> per icon of the catalog (65), the types
    // and the 18 colors in turn, every other icon by its short name, the first five with each date format: run n saves
    // version n, and its changes add the icon by its catalog name (and the date format). -f json then has version 65, the
    // last date format and for each type the color and the icon of its last run.
    [Test]
    [TestCase(506)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers(
        "project settings set",
        "--color",
        "--icon",
        "--date-format",
        "--color=red",
        "--color=orange",
        "--color=amber",
        "--color=yellow",
        "--color=lime",
        "--color=green",
        "--color=emerald",
        "--color=teal",
        "--color=cyan",
        "--color=sky",
        "--color=blue",
        "--color=indigo",
        "--color=violet",
        "--color=purple",
        "--color=fuchsia",
        "--color=pink",
        "--color=rose",
        "--color=gray",
        "--icon=i-lucide-target",
        "--icon=i-lucide-box",
        "--icon=i-lucide-sun",
        "--icon=i-lucide-link",
        "--icon=i-lucide-layers",
        "--icon=i-lucide-play",
        "--icon=i-lucide-cpu",
        "--icon=i-lucide-activity",
        "--icon=i-lucide-anchor",
        "--icon=i-lucide-atom",
        "--icon=i-lucide-bell",
        "--icon=i-lucide-bookmark",
        "--icon=i-lucide-bot",
        "--icon=i-lucide-boxes",
        "--icon=i-lucide-brain",
        "--icon=i-lucide-briefcase",
        "--icon=i-lucide-bug",
        "--icon=i-lucide-building-2",
        "--icon=i-lucide-calendar",
        "--icon=i-lucide-circle",
        "--icon=i-lucide-clock",
        "--icon=i-lucide-cloud",
        "--icon=i-lucide-code",
        "--icon=i-lucide-cog",
        "--icon=i-lucide-compass",
        "--icon=i-lucide-component",
        "--icon=i-lucide-container",
        "--icon=i-lucide-crown",
        "--icon=i-lucide-database",
        "--icon=i-lucide-factory",
        "--icon=i-lucide-file",
        "--icon=i-lucide-flag",
        "--icon=i-lucide-flame",
        "--icon=i-lucide-folder",
        "--icon=i-lucide-gauge",
        "--icon=i-lucide-gem",
        "--icon=i-lucide-git-branch",
        "--icon=i-lucide-globe",
        "--icon=i-lucide-heart",
        "--icon=i-lucide-hexagon",
        "--icon=i-lucide-key",
        "--icon=i-lucide-landmark",
        "--icon=i-lucide-layout-grid",
        "--icon=i-lucide-leaf",
        "--icon=i-lucide-lightbulb",
        "--icon=i-lucide-map-pin",
        "--icon=i-lucide-monitor",
        "--icon=i-lucide-network",
        "--icon=i-lucide-package",
        "--icon=i-lucide-puzzle",
        "--icon=i-lucide-rocket",
        "--icon=i-lucide-server",
        "--icon=i-lucide-shield",
        "--icon=i-lucide-smartphone",
        "--icon=i-lucide-square",
        "--icon=i-lucide-star",
        "--icon=i-lucide-store",
        "--icon=i-lucide-tag",
        "--icon=i-lucide-triangle",
        "--icon=i-lucide-truck",
        "--icon=i-lucide-user",
        "--icon=i-lucide-users",
        "--icon=i-lucide-workflow",
        "--icon=i-lucide-wrench",
        "--icon=i-lucide-zap",
        "--date-format=yyyy-mm-dd",
        "--date-format=dd.mm.yyyy",
        "--date-format=dd/mm/yyyy",
        "--date-format=mm/dd/yyyy",
        "--date-format=d mmm yyyy")]
    public async Task SettingsSet_EachColorIconAndDateFormat()
    {
        await CreateDisposableProjectAsync();
        var types = ProjectSettingsOutput.BuiltInTypes;
        var last = new Dictionary<string, (string Color, string Icon)>(StringComparer.Ordinal);

        for (var index = 0; index < ProjectSettingsOutput.Icons.Count; index++)
        {
            var code = types[index % types.Count].Code;
            var color = ProjectSettingsOutput.Colors[index % ProjectSettingsOutput.Colors.Count];
            var icon = ProjectSettingsOutput.Icons[index];
            var given = index % 2 == 1 ? icon["i-lucide-".Length..] : icon;
            var dateFormat = index < ProjectSettingsOutput.DateFormats.Count ? ProjectSettingsOutput.DateFormats[index] : null;
            string[] format = dateFormat is null ? [] : ["--date-format", dateFormat];

            var result = (await Sform(ProjectSettings(["set", "--type", code, "--color", color, "--icon", given, .. format])))
                .ShouldSucceed()
                .ShouldContain(ProjectSettingsOutput.Saved(index + 1));
            await Assert.That(ProjectSettingsOutput.HasChange(result, '+', $"\"Icon\": \"{icon}\"")).IsTrue().Because(result.PlainStdOut);
            if (dateFormat is not null)
            {
                await Assert.That(ProjectSettingsOutput.HasChange(result, '+', $"\"DateFormat\": \"{dateFormat}\"")).IsTrue().Because(result.PlainStdOut);
            }

            last[code] = (color, icon);
        }

        var settings = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.VersionOf(settings)).IsEqualTo((long)ProjectSettingsOutput.Icons.Count);
        await Assert.That(ProjectSettingsOutput.Value(settings, "DateFormat")).IsEqualTo(ProjectSettingsOutput.DateFormats[^1]);
        foreach (var (code, (color, icon)) in last)
        {
            await Assert.That(ProjectSettingsOutput.TypeValue(settings, code, "Color")).IsEqualTo(color).Because($"the color of {code}");
            await Assert.That(ProjectSettingsOutput.TypeValue(settings, code, "Icon")).IsEqualTo(icon).Because($"the icon of {code}");
        }
    }

    // ACC-PRS-09: In a terminal the tree and the dates follow the project settings; redirected output, --builtin and
    // SFORM_PROJECT_SETTINGS=builtin show the built-in ones.
    // In a disposable project with sbt.<s> and set --type sbt --label Company --date-format dd.mm.yyyy: in a terminal story
    // -t shows "<s> [Company]" under "Tree of subjects" and story history sbt.<s> its time as dd.MM.yyyy HH:mm; redirected,
    // in a terminal with --builtin and in a terminal with SFORM_PROJECT_SETTINGS=builtin the tree shows "<s> [subject]"
    // without "[Company]" and the history yyyy-MM-dd HH:mm. Every run exits 0; the settings stay at version 1.
    [Test]
    [TestCase(507)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("sform", "--builtin")]
    public async Task ConsoleOutput_FollowsSettingsInTerminal()
    {
        await CreateDisposableProjectAsync();
        var (subject, name) = await ArrangeSubjectAsync("--date-format", "dd.mm.yyyy");
        var named = Node(name, Company);
        var builtIn = Node(name, "subject");

        (await InTerminalAsync(null, InProject("story", "-t")))
            .ShouldMatch(Flexible("Tree of subjects"))
            .ShouldMatch(named)
            .ShouldNotMatch(builtIn);
        (await InTerminalAsync(null, InProject("story", "history", subject))).ShouldMatch(ProjectDate()).ShouldNotMatch(BuiltInDate());

        (await Sform(InProject("story", "-t"))).ShouldSucceed().ShouldMatch(builtIn).ShouldNotContain($"[{Company}]");
        (await Sform(InProject("story", "history", subject))).ShouldSucceed().ShouldMatch(BuiltInDate()).ShouldNotMatch(ProjectDate());

        var variable = new SformRunOptions { Environment = { ["SFORM_PROJECT_SETTINGS"] = "builtin" } };
        foreach (var (options, extra) in new[] { ((SformRunOptions?)null, new[] { "--builtin" }), (variable, Array.Empty<string>()) })
        {
            (await InTerminalAsync(options, [.. InProject("story", "-t"), .. extra])).ShouldMatch(builtIn).ShouldNotContain($"[{Company}]");
            (await InTerminalAsync(options, [.. InProject("story", "history", subject), .. extra])).ShouldMatch(BuiltInDate()).ShouldNotMatch(ProjectDate());
        }

        await Assert.That(ProjectSettingsOutput.VersionOf(await ReadSettingsAsync())).IsEqualTo(1L);
    }

    // ACC-PRS-10: account settings set and unset of output.projectSettings and output.icons switch the project's names and
    // the icons of the console output.
    // In a disposable project with sbt.<s> and set --type sbt --label Company --icon building-2, each tree (story -t) in a
    // terminal: output.projectSettings builtin shows "<s> [subject]" (sform.settings.json: output.projectSettings builtin),
    // Project (stored as project) "<s> [Company]"; output.icons always puts the icon before the tag, never leaves it out;
    // sometimes ends with 2 and "'sometimes' is no value of output.icons; the values are auto, always, never."; the list
    // shows both from "settings". The unsets print "output.icons is unset; sform uses auto." (then 42, "output.icons is not
    // set.") and "output.projectSettings is unset; sform uses project.", and the list shows auto and project from "default".
    [Test]
    [TestCase(508)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("account settings set", "arg0", "arg1", "arg0=output.icons", "arg0=output.projectSettings")]
    [Covers("account settings unset", "arg0", "arg0=output.icons", "arg0=output.projectSettings")]
    public async Task Preferences_ProjectSettingsAndIcons()
    {
        await CreateDisposableProjectAsync();
        var (_, name) = await ArrangeSubjectAsync("--icon", "building-2");
        var named = Node(name, Company);
        var builtIn = Node(name, "subject");

        // the icon between the name and the tag: an emoji, or "?" where the code page of the console has none
        var withIcon = new Regex($@"{Regex.Escape(name)}\s*\S+\s*\[{Company}\]", RegexOptions.CultureInvariant);

        (await Sform("account", "settings", "set", "output.projectSettings", "builtin")).ShouldSucceed().ShouldContain("output.projectSettings = builtin");
        await Assert.That(Home.ReadSettings()?["output"]?["projectSettings"]?.GetValue<string>()).IsEqualTo("builtin");
        (await InTerminalAsync(null, InProject("story", "-t"))).ShouldMatch(builtIn).ShouldNotContain($"[{Company}]");

        (await Sform("account", "settings", "set", "output.projectSettings", "Project")).ShouldSucceed().ShouldContain("output.projectSettings = project");
        (await InTerminalAsync(null, InProject("story", "-t"))).ShouldMatch(named);

        (await Sform("account", "settings", "set", "output.icons", "always")).ShouldSucceed().ShouldContain("output.icons = always");
        (await InTerminalAsync(null, InProject("story", "-t"))).ShouldMatch(withIcon).ShouldNotMatch(named);

        (await Sform("account", "settings", "set", "output.icons", "never")).ShouldSucceed().ShouldContain("output.icons = never");
        (await InTerminalAsync(null, InProject("story", "-t"))).ShouldMatch(named).ShouldNotMatch(withIcon);

        (await Sform("account", "settings", "set", "output.icons", "sometimes"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("'sometimes' is no value of output.icons; the values are auto, always, never.");
        var stored = (await Sform("account", "settings")).ShouldSucceed();
        await AssertPreferenceAsync(stored, "output.icons", "never", "settings");
        await AssertPreferenceAsync(stored, "output.projectSettings", "project", "settings");

        (await Sform("account", "settings", "unset", "output.icons")).ShouldSucceed().ShouldContain("output.icons is unset; sform uses auto.");
        (await Sform("account", "settings", "unset", "output.icons")).ShouldExitWith(CliExit.NoWorkNeeded).ShouldContain("output.icons is not set.");
        (await Sform("account", "settings", "unset", "output.projectSettings")).ShouldSucceed().ShouldContain("output.projectSettings is unset; sform uses project.");
        var defaults = (await Sform("account", "settings")).ShouldSucceed();
        await AssertPreferenceAsync(defaults, "output.icons", "auto", "default");
        await AssertPreferenceAsync(defaults, "output.projectSettings", "project", "default");
    }

    // ACC-PRS-11: The project settings commands act on the project of --projectKey and ignore -v and --view.
    // In a disposable project <P>, with a generated view name <view> that is no view of it: set --projectKey <P> -v <view>
    // --date-format dd/mm/yyyy and set --projectKey <P> --view <view> --type exe --label Job save versions 1 and 2;
    // project settings --projectKey <P> -v <view> -f json and --projectKey <P> --view <view> show them; edit --projectKey
    // <P> -v <view> and --view <view> in a terminal open the settings of <P> (dd/mm/yyyy) and end with "No changes
    // detected." (42); reset --projectKey <P> -v <view> --date-format -y saves version 3, the same with --view ends with 42
    // ("did not change; they stay at version 3.").
    [Test]
    [TestCase(509)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("project settings", "--projectKey", "-v", "--view")]
    [Covers("project settings set", "--projectKey", "-v", "--view")]
    [Covers("project settings edit", "--projectKey", "-v", "--view")]
    [Covers("project settings reset", "--projectKey", "-v", "--view")]
    public async Task Settings_ContextOptions_TargetProject()
    {
        var project = await CreateDisposableProjectAsync();
        var view = CliData.ViewName();

        (await Sform("project", "settings", "set", "--projectKey", project, "-v", view, "--date-format", "dd/mm/yyyy")).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(1));
        (await Sform("project", "settings", "set", "--projectKey", project, "--view", view, "--type", "exe", "--label", "Job")).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(2));

        var json = (await Sform("project", "settings", "--projectKey", project, "-v", view, "-f", "json")).ShouldSucceed().JsonNode();
        await Assert.That(ProjectSettingsOutput.VersionOf(json)).IsEqualTo(2L);
        await Assert.That(ProjectSettingsOutput.Value(json, "DateFormat")).IsEqualTo("dd/mm/yyyy");
        await Assert.That(ProjectSettingsOutput.TypeValue(json, "exe", "Label")).IsEqualTo("Job");
        (await Sform("project", "settings", "--projectKey", project, "--view", view))
            .ShouldSucceed()
            .ShouldHaveLine($"# Project settings of {project}")
            .ShouldHaveLine("Version: 2");

        var editor = await UseEditorAsync();
        foreach (var option in new[] { "-v", "--view" })
        {
            var unchanged = editor.With(EditorChange.None);
            await using (var terminal = await StartSformInTerminal(unchanged, "project", "settings", "edit", "--projectKey", project, option, view))
            {
                await terminal.ExpectAsync("No changes detected.");
                (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.NoWorkNeeded);
            }

            var opened = editor.ReadLog(unchanged).Single().OriginalJson()!;
            await Assert.That(ProjectSettingsOutput.Value(opened, "DateFormat")).IsEqualTo("dd/mm/yyyy");
            await Assert.That(ProjectSettingsOutput.TypeValue(opened, "exe", "Label")).IsEqualTo("Job");
        }

        (await Sform("project", "settings", "reset", "--projectKey", project, "-v", view, "--date-format", "-y")).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(3));
        (await Sform("project", "settings", "reset", "--projectKey", project, "--view", view, "--date-format", "-y"))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain(ProjectSettingsOutput.Unchanged(3));

        var reset = await ReadSettingsAsync();
        await Assert.That(ProjectSettingsOutput.VersionOf(reset)).IsEqualTo(3L);
        await Assert.That(ProjectSettingsOutput.Value(reset, "DateFormat")).IsNull();
        await Assert.That(ProjectSettingsOutput.TypeValue(reset, "exe", "Label")).IsEqualTo("Job");
    }

    // A row of the list of sform account settings, "  <key>  <value>  <source>" (as AccountSettingsTests reads it).
    private static async Task AssertPreferenceAsync(SformResult result, string key, string value, string source)
    {
        var row = result.Lines
            .Select(line => Columns().Split(line.Trim()))
            .FirstOrDefault(cells => cells.Length == 3 && cells[0] == key)
            ?? throw result.Fail($"listed no setting {key} with a value and a source");
        await Assert.That(row[1]).IsEqualTo(value).Because($"the value of {key}");
        await Assert.That(row[2]).IsEqualTo(source).Because($"the source of {key}");
    }

    // The options of a run whose temporary folder (TEMP, TMP, TMPDIR) is the folder: sform writes the editor's file and
    // the edit it keeps there. The terminal is 1000 columns wide, so the line that names the kept file is not wrapped.
    private static SformRunOptions InTemporaryFolder(string folder)
    {
        return new SformRunOptions
        {
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TEMP"] = folder,
                ["TMP"] = folder,
                ["TMPDIR"] = folder,
            },
            TerminalColumns = 1000,
        };
    }

    // The text with any run of white space matching any white space or none, as SformProcess.ExpectAsync reads a terminal.
    private static Regex Flexible(string text)
    {
        return new Regex(string.Join(@"\s*", Regex.Split(text.Trim(), @"\s+").Select(Regex.Escape)), RegexOptions.CultureInvariant);
    }

    // A node of story -t: its name, then its tag ("<name> [Company]", "<name> [subject]"), nothing in between.
    private static Regex Node(string name, string tag)
    {
        return new Regex($@"{Regex.Escape(name)}\s*\[{Regex.Escape(tag)}\]", RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    // "Modified: <yyyy-MM-dd HH:mm> by <actor>" of the table (the built-in date: its output is redirected).
    // "v<old>→v<new>:" of the detail of a ProjectSettingsChange; the arrow may come out as another character in a code page.
    [GeneratedRegex(@"v(\d+)\S{1,3}v(\d+):")]
    private static partial Regex VersionChange();

    [GeneratedRegex(@"(?m)^\s*Modified: \d{4}-\d{2}-\d{2} \d{2}:\d{2} by \S")]
    private static partial Regex ModifiedLine();

    // A time of a table (to the minute) in the project's date format dd.mm.yyyy, with the time as always.
    [GeneratedRegex(@"\b\d{2}\.\d{2}\.\d{4}\s*\d{2}:\d{2}(?![:\d])")]
    private static partial Regex ProjectDate();

    // A time of a table (to the minute) in the built-in format; not the instants of the history's changes, which have
    // seconds.
    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\s*\d{2}:\d{2}(?![:\d])")]
    private static partial Regex BuiltInDate();

    // project settings [arguments] -p <the acting project>.
    private string[] ProjectSettings(params string[] arguments)
    {
        return ["project", "settings", .. arguments, "-p", ProjectKey];
    }

    // project settings -f json of the acting project.
    private async Task<JsonNode> ReadSettingsAsync()
    {
        return (await Sform(new SformRunOptions { Label = "the project settings" }, ProjectSettings("-f", "json"))).ShouldSucceed().JsonNode();
    }

    // A subject of the acting project (its key and its name) whose type the project names Company, with the options of
    // the set (version 1).
    private async Task<(string Key, string Name)> ArrangeSubjectAsync(params string[] options)
    {
        var key = CliData.AnnotationKey(CliData.Subject);
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(ProjectSettings(["set", "--type", "sbt", "--label", Company, .. options]))).ShouldSucceed().ShouldContain(ProjectSettingsOutput.Saved(1));
        return (key, key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..]);
    }

    // A run in a pseudo terminal to its end, which must succeed.
    private async Task<SformResult> InTerminalAsync(SformRunOptions? options, params string[] arguments)
    {
        await using var terminal = await StartSformInTerminal(options ?? new SformRunOptions(), arguments);
        return (await terminal.WaitForExitAsync()).ShouldSucceed();
    }

    // A folder of the working directory for InTemporaryFolder.
    private string TemporaryFolder(string name)
    {
        var folder = FileSystem.Path.Combine(WorkingDirectory, "tmp", name);
        FileSystem.Directory.CreateDirectory(folder);
        return folder;
    }

    // The one file sform kept the edit in (EditorService.CreateTempFilePath: project-settings-<org>.<project>-<random>.json;
    // the editor's own file of the same name is deleted after the editor).
    private string KeptEdit(string folder, string projectKey)
    {
        var files = FileSystem.Directory.GetFiles(folder, $"project-settings-{projectKey}-*.json");
        return files.Length == 1
            ? files[0]
            : throw new InvalidOperationException($"sform should have kept the edit of the settings of {projectKey} in one file of {folder}, which has {(files.Length == 0 ? "none" : string.Join(", ", files))}.");
    }
}
