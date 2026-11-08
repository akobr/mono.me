using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// Export and Import: sform story export and sform story import of a view (docs/Platform/Storyteller/export-import.md).
// Both need an Administrator, so the Owner cases work in a disposable project, whose default view they seed with
// SeedViewAsync: annotations, a type schema, a template and a configuration with versions.
[Area(Area.Annotations)]
public sealed partial class AnnotationTransferTests : CliTest
{
    // ANN-TRN-01: story export saves the view as a folder zip with the server's file name.
    // The Owner seeds the disposable project's default view (<rst> with a unit, <rst2>, two versions of the configuration
    // of <rst>, two of the template of rst, the type schema of rst). story export says "Exporting the annotations,
    // schemas, templates and configurations of <org>.<project>.default (folder)" and "Exported 3 annotations, 1 schema,
    // 1 template and 1 configuration (…) to export-<org>-<project>-default-<time>Z.zip.", and the zip in the working
    // folder holds export.json and the JSON files of the four kinds, with the earlier versions of the template and the
    // configuration.
    [Test]
    [TestCase(341)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story export")]
    public async Task Export_Default_SavesZip()
    {
        await CreateDisposableProjectAsync();
        var seeded = await SeedViewAsync();

        var export = (await Sform(InProject("story", "export")))
            .ShouldSucceed()
            .ShouldContain($"Exporting the annotations, schemas, templates and configurations of {ProjectKey}.{View} (folder)")
            .ShouldContain("Exported 3 annotations, 1 schema, 1 template and 1 configuration (");
        var zip = DestinationOf(export);
        var name = ExportedPattern().Match(AnnotationOutput.LinesWith(export, "Exported ")[^1]).Groups["path"].Value;
        await Assert.That(Regex.IsMatch(name, $"^export-{Regex.Escape(Organization)}-{Regex.Escape(Project)}-{Regex.Escape(View)}-\\d{{8}}-\\d{{6}}Z\\.zip$")).IsTrue();
        await Assert.That(FileSystem.File.Exists(FileSystem.Path.Combine(WorkingDirectory, name))).IsTrue();

        var entries = ViewArchive.Entries(FileSystem, zip);
        await AssertFolderExportAsync(entries, seeded);
        var manifest = JsonNode.Parse(ViewArchive.ReadEntry(FileSystem, zip, "export.json")!)!;
        await Assert.That((manifest["Include"] as JsonArray)!.Select(kind => kind!.GetValue<string>())).IsEquivalentTo(new[] { "Annotations", "Schemas", "Templates", "Configurations" });
        var configuration = JsonNode.Parse(ViewArchive.ReadEntry(FileSystem, zip, $"configurations/{seeded.Responsibility}.conf.json")!)!;
        await Assert.That(configuration["conf"]?.GetValue<string>()).IsEqualTo(seeded.ConfigurationValue);
    }

    // ANN-TRN-02: -f git exports a git repository with every version as a commit.
    // The Owner seeds the view; story export -f git saves export-…-git-<time>Z.zip, a repository (.git with one pack) with
    // the working tree of the folder format, whose history has a commit per version: one per annotation (3), the two
    // versions of the configuration of <rst>, the two of the template of rst and the one of the type schema, 8 in all,
    // with the messages of export-import.md ("Add annotation <rst>", "Update configuration of <rst> to version …", …).
    // story export --format folder saves the folder zip as in ANN-TRN-01.
    [Test]
    [TestCase(342)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story export", "-f", "--format", "--format=git", "--format=folder")]
    public async Task Export_Git_HasCommits()
    {
        await CreateDisposableProjectAsync();
        var seeded = await SeedViewAsync();

        var git = DestinationOf((await Sform(InProject("story", "export", "-f", "git"))).ShouldSucceed().ShouldContain($"of {ProjectKey}.{View} (git)"));
        await Assert.That(FileSystem.Path.GetFileName(git)).Contains($"-{View}-git-");
        var entries = ViewArchive.Entries(FileSystem, git);
        await Assert.That(entries).Contains(".git/HEAD");
        await Assert.That(entries).Contains($"annotations/{seeded.Responsibility}.json");
        await Assert.That(entries).Contains("templates/rst.template.json");
        await Assert.That(entries).Contains($"configurations/{seeded.Responsibility}.conf.json");

        var subjects = ViewArchive.CommitSubjects(FileSystem, git);
        await Assert.That(subjects.Count).IsEqualTo(8);
        foreach (var key in new[] { seeded.Responsibility, seeded.Unit, seeded.Other })
        {
            await Assert.That(subjects).Contains($"Add annotation {key}");
        }

        await Assert.That(subjects.Count(subject => subject.StartsWith($"Add configuration of {seeded.Responsibility} (version ", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(subjects.Count(subject => subject.StartsWith($"Update configuration of {seeded.Responsibility} to version ", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(subjects.Count(subject => subject.StartsWith("Add template rst (version ", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(subjects.Count(subject => subject.StartsWith("Update template rst to version ", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(subjects.Count(subject => subject.StartsWith("Add type schema rst (version ", StringComparison.Ordinal))).IsEqualTo(1);

        var folder = DestinationOf((await Sform(InProject("story", "export", "--format", "folder"))).ShouldSucceed().ShouldContain($"of {ProjectKey}.{View} (folder)"));
        await Assert.That(FileSystem.Path.GetFileName(folder)).DoesNotContain("-git-");
        await AssertFolderExportAsync(ViewArchive.Entries(FileSystem, folder), seeded);
    }

    // ANN-TRN-03: -i limits the kinds, comma-separated or repeated.
    // The Owner seeds the view; story export -i annotations,schemas saves a zip with only annotations/ and schemas/
    // ("Exporting the annotations and schemas of …"); story export --include templates --include configurations one
    // with only templates/ and configurations/.
    [Test]
    [TestCase(343)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story export", "-i", "--include", "--include=annotations", "--include=schemas", "--include=templates", "--include=configurations")]
    public async Task Export_IncludeKinds_Limits()
    {
        await CreateDisposableProjectAsync();
        await SeedViewAsync();

        var first = DestinationOf((await Sform(InProject("story", "export", "-i", "annotations,schemas")))
            .ShouldSucceed()
            .ShouldContain($"Exporting the annotations and schemas of {ProjectKey}.{View}"));
        await Assert.That(FoldersOf(ViewArchive.Entries(FileSystem, first))).IsEquivalentTo(new[] { "annotations", "schemas" });

        var second = DestinationOf((await Sform(InProject("story", "export", "--include", "templates", "--include", "configurations")))
            .ShouldSucceed()
            .ShouldContain($"Exporting the templates and configurations of {ProjectKey}.{View}"));
        await Assert.That(FoldersOf(ViewArchive.Entries(FileSystem, second))).IsEquivalentTo(new[] { "templates", "configurations" });
    }

    // ANN-TRN-04: -o saves to a file or a folder, and -x unpacks into an empty folder.
    // The Owner seeds the view; story export -o out.zip saves out.zip; --output dir/ saves the server's file name in dir
    // (created); -x -o unpacked and --extract --output unpacked2 unpack the archive into those folders ("Exported … into
    // unpacked."), with export.json and annotations/<rst>.json.
    [Test]
    [TestCase(344)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story export", "-o", "--output", "-x", "--extract")]
    public async Task Export_OutputAndExtract()
    {
        await CreateDisposableProjectAsync();
        var seeded = await SeedViewAsync();

        var file = DestinationOf((await Sform(InProject("story", "export", "-o", "out.zip"))).ShouldSucceed());
        await Assert.That(FileSystem.Path.GetFileName(file)).IsEqualTo("out.zip");
        await Assert.That(ViewArchive.Entries(FileSystem, file)).Contains("export.json");

        var inFolder = DestinationOf((await Sform(InProject("story", "export", "--output", "dir/"))).ShouldSucceed());
        await Assert.That(FileSystem.Path.GetFileName(FileSystem.Path.GetDirectoryName(inFolder))).IsEqualTo("dir");
        await Assert.That(FileSystem.Path.GetFileName(inFolder)).StartsWith($"export-{Organization}-{Project}-{View}-");
        await Assert.That(ViewArchive.Entries(FileSystem, inFolder)).Contains("export.json");

        foreach (var (options, folder) in new[] { (new[] { "-x", "-o" }, "unpacked"), (new[] { "--extract", "--output" }, "unpacked2") })
        {
            var unpacked = (await Sform(InProject(["story", "export", options[0], options[1], folder]))).ShouldSucceed().ShouldContain($"into {folder}.");
            await Assert.That(DestinationOf(unpacked)).IsEqualTo(FileSystem.Path.GetFullPath(FileSystem.Path.Combine(WorkingDirectory, folder)));
            await Assert.That(FileSystem.File.Exists(FileSystem.Path.Combine(WorkingDirectory, folder, "export.json"))).IsTrue();
            await Assert.That(FileSystem.File.Exists(FileSystem.Path.Combine(WorkingDirectory, folder, "annotations", $"{seeded.Responsibility}.json"))).IsTrue();
        }
    }

    // ANN-TRN-05: --timeout stops waiting while the export continues on the server.
    // The Owner seeds the view; story export --timeout 0 reads the job once and stops waiting: exit 40 and "The export
    // <id> is still queued|running; it continues on the server.", with no zip written. sform reads no job by its id, so
    // the end of the job on the server shows only as a later story export of the view that succeeds.
    [Test]
    [TestCase(345)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story export", "--timeout")]
    public async Task Export_Timeout_StopsWaiting()
    {
        await CreateDisposableProjectAsync();
        await SeedViewAsync();

        var stopped = (await Sform(InProject("story", "export", "--timeout", "0"))).ShouldExitWith(CliExit.Aborted);
        if (!StillRunningPattern().IsMatch(stopped.PlainStdOut))
        {
            throw stopped.Fail("did not say that the export is still queued or running and continues on the server");
        }

        await Assert.That(FileSystem.Directory.EnumerateFiles(WorkingDirectory, "export-*.zip").Any()).IsFalse();

        var later = DestinationOf((await Sform(InProject("story", "export"))).ShouldSucceed());
        await Assert.That(FileSystem.File.Exists(later)).IsTrue();
    }

    // ANN-TRN-06: story import imports a zip into another view, all four kinds or those of -i.
    // The Owner seeds view A (default) and exports it as a.zip. story import a.zip -p … -v <B> imports it into a new view
    // B: the report "# Import of a.zip" lists the created documents (<rst> Created), and B has the annotations, the
    // configuration of <rst> and the template of rst. story import a.zip -p … -v <C> -i annotations,templates imports
    // into C only those two kinds ("(annotations and templates)"; the schema and the configuration are the "2 files
    // excluded"), so C has <rst> and the template's properties, but not the configuration's.
    [Test]
    [TestCase(346)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story import", "arg0", "--include=annotations", "--include=templates")]
    public async Task Import_Zip_ImportsView()
    {
        await CreateDisposableProjectAsync();
        var seeded = await SeedViewAsync();
        await ExportAsync("-o", "a.zip");
        var viewB = CliData.ViewName();
        var viewC = CliData.ViewName();

        var intoB = (await Sform("story", "import", "a.zip", "-p", ProjectKey, "-v", viewB))
            .ShouldSucceed()
            .ShouldContain($"Importing a.zip into {ProjectKey}.{viewB}")
            .ShouldHaveLine("# Import of a.zip");
        await Assert.That(AnnotationOutput.LinesWith(intoB, seeded.Responsibility).Any(line => line.Contains("Created", StringComparison.Ordinal))).IsTrue();
        (await Sform("story", "get", seeded.Unit, "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        var contentB = (await Sform("story", "config", seeded.Responsibility, "-p", ProjectKey, "-v", viewB, "--content-only")).ShouldSucceed().JsonNode();
        await Assert.That(contentB["conf"]?.GetValue<string>()).IsEqualTo(seeded.ConfigurationValue);
        (await Sform("story", "template", "rst", "-p", ProjectKey, "-v", viewB)).ShouldSucceed().ShouldContain(seeded.TemplateValue);

        var intoC = (await Sform("story", "import", "a.zip", "-p", ProjectKey, "-v", viewC, "-i", "annotations,templates"))
            .ShouldSucceed()
            .ShouldContain($"Importing a.zip into {ProjectKey}.{viewC} (annotations and templates)")
            .ShouldContain("2 files excluded");
        await Assert.That(intoC.Lines.Any(line => line.Trim().StartsWith("Configurations ", StringComparison.Ordinal))).IsFalse();
        (await Sform("story", "get", seeded.Responsibility, "-p", ProjectKey, "-v", viewC)).ShouldSucceed();
        var contentC = (await Sform("story", "config", seeded.Responsibility, "-p", ProjectKey, "-v", viewC, "--content-only")).ShouldSucceed().JsonNode();
        await Assert.That(contentC["tmpl"]?.GetValue<string>()).IsEqualTo(seeded.TemplateValue);
        await Assert.That(contentC["conf"]).IsNull();
    }

    // ANN-TRN-07: import of a folder packs its JSON files without hidden files.
    // A folder holds annotations/<rst1>.json and <rst2>.json, plus .git/<rst3>.json and ._<rst4>.json; story import
    // folder/ uploads folder.zip ("Importing folder.zip into …") with "0 files excluded, 0 other files ignored.": <rst1>
    // and <rst2> exist afterwards, <rst3> and <rst4> do not.
    [Test]
    [TestCase(347)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story import", "arg0")]
    public async Task Import_Folder_SkipsHidden()
    {
        await CreateDisposableProjectAsync();
        var visible = new[] { CliData.ResponsibilityKey(), CliData.ResponsibilityKey() };
        var hidden = new[] { CliData.ResponsibilityKey(), CliData.ResponsibilityKey() };
        await WriteFileAsync($"folder/annotations/{visible[0]}.json", new JsonObject { ["Title"] = CliData.Value() }.ToJsonString());
        await WriteFileAsync($"folder/{visible[1]}.json", "{}");
        await WriteFileAsync($"folder/.git/{hidden[0]}.json", "{}");
        await WriteFileAsync($"folder/._{hidden[1]}.json", "{}");

        (await Sform(InProject("story", "import", "folder/")))
            .ShouldSucceed()
            .ShouldContain($"Importing folder.zip into {ProjectKey}.{View}")
            .ShouldContain("0 files excluded, 0 other files ignored.");

        foreach (var key in visible)
        {
            (await Sform(InProject("story", "get", key))).ShouldSucceed();
        }

        foreach (var key in hidden)
        {
            (await Sform(InProject("story", "get", key)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The annotation '{key}' has not been found.");
        }
    }

    // ANN-TRN-08: -e skip keeps existing documents, -e overwrite writes new versions.
    // The Owner seeds view A, exports it as a.zip and imports it into a new view B, then changes the configuration of
    // <rst> in B. story import a.zip -e skip ("skip existing") reports the configuration Skipped with DocumentExists and
    // keeps the change; story import a.zip --existing overwrite reports it Updated and B has A's content again, as a new
    // version.
    [Test]
    [TestCase(348)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story import", "-e", "--existing", "--existing=skip", "--existing=overwrite")]
    public async Task Import_ExistingMode_SkipOrOverwrite()
    {
        await CreateDisposableProjectAsync();
        var seeded = await SeedViewAsync();
        await ExportAsync("-o", "a.zip");
        var viewB = CliData.ViewName();
        var changed = CliData.Value();
        (await Sform("story", "import", "a.zip", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        (await Sform("story", "config", "set", seeded.Responsibility, "-x", $"conf={changed}", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        var versions = (await Sform("story", "config", "versions", seeded.Responsibility, "-p", ProjectKey, "-v", viewB)).ShouldSucceed();

        var skip = (await Sform("story", "import", "a.zip", "-e", "skip", "-p", ProjectKey, "-v", viewB))
            .ShouldSucceed()
            .ShouldContain("skip existing");
        await Assert.That(ConfigurationRowOf(skip, seeded.Responsibility)).Contains("Skipped");
        await Assert.That(ConfigurationRowOf(skip, seeded.Responsibility)).Contains("DocumentExists");
        await Assert.That(await ConfigurationValueAsync(seeded.Responsibility, viewB)).IsEqualTo(changed);

        var overwrite = (await Sform("story", "import", "a.zip", "--existing", "overwrite", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        await Assert.That(ConfigurationRowOf(overwrite, seeded.Responsibility)).Contains("Updated");
        await Assert.That(await ConfigurationValueAsync(seeded.Responsibility, viewB)).IsEqualTo(seeded.ConfigurationValue);
        var newer = (await Sform("story", "config", "versions", seeded.Responsibility, "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        await Assert.That(VersionRows(newer)).IsEqualTo(VersionRows(versions) + 1);
    }

    // ANN-TRN-09: --dry-run reports every outcome and writes nothing.
    // The Owner seeds view A, exports it as a.zip and imports it into a new view B; in B it changes the configuration of
    // <rst> and deletes <rst2>. story import a.zip --dry-run ("dry run") reports with "Would create" and "Would update":
    // <rst2> Created with the warning AnnotationRecreated, the configuration of <rst> Updated, and "Nothing was written
    // (dry run)."; story and story config versions <rst> of B print what they printed before, the configuration keeps the
    // change and <rst2> stays deleted.
    [Test]
    [TestCase(350)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story import", "--dry-run")]
    public async Task Import_DryRun_WritesNothing()
    {
        await CreateDisposableProjectAsync();
        var seeded = await SeedViewAsync();
        await ExportAsync("-o", "a.zip");
        var viewB = CliData.ViewName();
        var changed = CliData.Value();
        (await Sform("story", "import", "a.zip", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        (await Sform("story", "config", "set", seeded.Responsibility, "-x", $"conf={changed}", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        (await Sform("story", "delete", seeded.Other, "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        var listBefore = (await Sform("story", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        var versionsBefore = (await Sform("story", "config", "versions", seeded.Responsibility, "-p", ProjectKey, "-v", viewB)).ShouldSucceed();

        var dryRun = (await Sform("story", "import", "a.zip", "--dry-run", "-p", ProjectKey, "-v", viewB))
            .ShouldSucceed()
            .ShouldContain("dry run")
            .ShouldContain("Would create")
            .ShouldContain("Would update")
            .ShouldContain("AnnotationRecreated")
            .ShouldContain("Nothing was written (dry run).");
        await Assert.That(AnnotationOutput.LinesWith(dryRun, seeded.Other).Any(line => line.Contains("Created", StringComparison.Ordinal))).IsTrue();
        await Assert.That(ConfigurationRowOf(dryRun, seeded.Responsibility)).Contains("Updated");

        var listAfter = (await Sform("story", "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        await Assert.That(AnnotationOutput.Keys(listAfter)).IsEquivalentTo(AnnotationOutput.Keys(listBefore));
        await Assert.That(AnnotationOutput.Keys(listAfter)).DoesNotContain(seeded.Other);
        var versionsAfter = (await Sform("story", "config", "versions", seeded.Responsibility, "-p", ProjectKey, "-v", viewB)).ShouldSucceed();
        await Assert.That(versionsAfter.PlainStdOut).IsEqualTo(versionsBefore.PlainStdOut);
        await Assert.That(await ConfigurationValueAsync(seeded.Responsibility, viewB)).IsEqualTo(changed);
        (await Sform("story", "get", seeded.Other, "-p", ProjectKey, "-v", viewB))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{seeded.Other}' is deleted (");
    }

    // ANN-TRN-10: Without --force a breaking schema fails and the rest imports; --force stores it with a warning.
    // In the disposable project <rst> has the configuration a=1 (a number) and <rst2> none. x.zip holds the type schema
    // of rst that wants a as a string and a configuration of <rst2>. story import x.zip -i schemas,configurations
    // --timeout 10 ends with 2: the type schema Failed with SchemaViolation, the configuration of <rst2> Created (and
    // stored). story import x.zip --include schemas --force stores the schema (Created) with the warning
    // SchemaViolation, and story schema type get rst returns it.
    [Test]
    [TestCase(352)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story import", "--force", "-i", "--include", "--timeout", "--include=schemas", "--include=configurations")]
    public async Task Import_Force_StoresBreaking()
    {
        await CreateDisposableProjectAsync();
        var breaking = CliData.ResponsibilityKey();
        var target = CliData.ResponsibilityKey();
        var value = CliData.Value();
        await Sform(InProject("story", "set", breaking)).ShouldSucceed();
        await Sform(InProject("story", "config", "set", breaking, "-x", "a=1")).ShouldSucceed();
        await Sform(InProject("story", "set", target)).ShouldSucceed();
        ViewArchive.Write(FileSystem, FileSystem.Path.Combine(WorkingDirectory, "x.zip"), new Dictionary<string, string>
        {
            ["schemas/rst.schema.json"] = """{ "type": "object", "properties": { "a": { "type": "string" } } }""",
            [$"configurations/{target}.conf.json"] = new JsonObject { ["c"] = value }.ToJsonString(),
        });

        var unforced = (await Sform(InProject("story", "import", "x.zip", "-i", "schemas,configurations", "--timeout", "10")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("(schemas and configurations)");
        var failedSchema = TypeSchemaRowOf(unforced);
        await Assert.That(failedSchema).Contains("Failed");
        await Assert.That(failedSchema).Contains("SchemaViolation");
        await Assert.That(ConfigurationRowOf(unforced, target)).Contains("Created");
        var stored = (await Sform(InProject("story", "config", target, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(stored["c"]?.GetValue<string>()).IsEqualTo(value);

        var forced = (await Sform(InProject("story", "import", "x.zip", "--include", "schemas", "--force")))
            .ShouldSucceed()
            .ShouldContain("forced")
            .ShouldContain("SchemaViolation");
        var storedSchema = TypeSchemaRowOf(forced);
        await Assert.That(storedSchema).Contains("Created");
        (await Sform(InProject("story", "schema", "type", "get", "rst"))).ShouldSucceed().ShouldContain("\"string\"");
    }

    // ANN-TRN-11: Export and import by a Contributor are refused; a machine needs Configuration.Transfer.
    // As a Contributor, story export and story import <folder> end with 44 and the server's "No Administrator access to
    // '<project>'.". An Administrator creates <rst> in a view of its own in Target:Project and two API key machines there,
    // one with -r and one with -m ConfigurationTransfer (which adds the read scopes). As the -r machine story export ends
    // with 44 and names Configuration.Transfer, and story import ends with 44; as the other machine story export -o
    // machine.zip succeeds and the zip has annotations/<rst>.json.
    [Test]
    [TestCase(354)]
    [UserRole(Role.Contributor)]
    [Covers("story export")]
    [Covers("story import")]
    public async Task Transfer_RolesAndScopes()
    {
        await WriteFileAsync($"transfer/{CliData.ResponsibilityKey()}.json", "{}");
        (await Sform(InProject("story", "export")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Administrator access to '{ProjectKey}'.");
        (await Sform(InProject("story", "import", "transfer")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Administrator access to '{ProjectKey}'.");

        var admin = await AsAnotherUserAsync(AdminUser);
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        (await admin.Sform("story", "set", key, "-p", Target.ProjectKey, "-v", view)).ShouldSucceed();
        var creation = new MachineCreation { As = admin, ProjectKey = Target.ProjectKey, View = view };
        var reader = await CreateMachineAsync(creation, "-r", "-k", "ApiKey");
        var transfer = await CreateMachineAsync(creation, "-m", "ConfigurationTransfer", "-k", "ApiKey");

        (await Sform(AsMachine(reader.SignInCredentials), "story", "export"))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("Configuration.Transfer");
        (await Sform(AsMachine(reader.SignInCredentials), "story", "import", "transfer")).ShouldExitWith(CliExit.Unauthorized);

        (await Sform(AsMachine(transfer.SignInCredentials), "story", "export", "-o", "machine.zip")).ShouldSucceed();
        await Assert.That(ViewArchive.Entries(FileSystem, FileSystem.Path.Combine(WorkingDirectory, "machine.zip"))).Contains($"annotations/{key}.json");
    }

    // The full path of the zip (or the folder of -x) that "Exported … to|into <path>." names.
    private string DestinationOf(SformResult export)
    {
        var line = export.Lines.Select(text => ExportedPattern().Match(text.Trim())).LastOrDefault(match => match.Success)
            ?? throw export.Fail("did not say where it exported to");
        return FileSystem.Path.GetFullPath(FileSystem.Path.Combine(WorkingDirectory, line.Groups["path"].Value));
    }

    // story export with the options in the acting project, which must succeed; the full path of the zip.
    private async Task<string> ExportAsync(params string[] options)
    {
        return DestinationOf((await Sform(InProject(["story", "export", .. options]))).ShouldSucceed());
    }

    // The default view of the acting project gets <rst> with a unit, <rst2>, the configuration of <rst> in two versions
    // (conf), the template of rst in two versions (tmpl, then tmpl2 merged into it) and the type schema of rst, which the
    // configuration complies with.
    private async Task<SeededView> SeedViewAsync()
    {
        var responsibility = CliData.ResponsibilityKey();
        var seeded = new SeededView(responsibility, CliData.UnitKeyOf(responsibility), CliData.ResponsibilityKey(), CliData.Value(), CliData.Value());
        foreach (var key in new[] { seeded.Responsibility, seeded.Unit, seeded.Other })
        {
            await Sform(InProject("story", "set", key)).ShouldSucceed();
        }

        await Sform(InProject("story", "config", "set", seeded.Responsibility, "-x", $"conf={CliData.Value()}")).ShouldSucceed();
        await Sform(InProject("story", "config", "set", seeded.Responsibility, "-x", $"conf={seeded.ConfigurationValue}")).ShouldSucceed();
        await Sform(InProject("story", "template", "set", "rst", "-x", $"tmpl={seeded.TemplateValue}")).ShouldSucceed();
        await Sform(InProject("story", "template", "set", "rst", "-x", $"tmpl2={CliData.Value()}")).ShouldSucceed();
        await WriteFileAsync("rst.schema.json", """{ "type": "object", "properties": { "conf": { "type": "string" } } }""");
        await Sform(InProject("story", "schema", "type", "set", "rst", "-i", "rst.schema.json")).ShouldSucceed();
        return seeded;
    }

    // The entries of a folder export of the seeded view: the manifest and every current document, with an earlier version
    // of the configuration and of the template.
    private async Task AssertFolderExportAsync(IReadOnlyList<string> entries, SeededView seeded)
    {
        foreach (var entry in new[]
        {
            "export.json",
            $"annotations/{seeded.Responsibility}.json",
            $"annotations/{seeded.Unit}.json",
            $"annotations/{seeded.Other}.json",
            "schemas/rst.schema.json",
            "templates/rst.template.json",
            $"configurations/{seeded.Responsibility}.conf.json",
        })
        {
            await Assert.That(entries).Contains(entry);
        }

        var configurationVersion = new Regex($"^configurations/{Regex.Escape(seeded.Responsibility)}\\.conf\\.\\d+\\.json$", RegexOptions.CultureInvariant);
        await Assert.That(entries.Any(entry => configurationVersion.IsMatch(entry))).IsTrue();
        await Assert.That(entries.Any(entry => TemplateVersionPattern().IsMatch(entry))).IsTrue();
    }

    // The top folders of the documents of an export (export.json left out).
    private static IReadOnlyList<string> FoldersOf(IReadOnlyList<string> entries)
    {
        return entries.Where(entry => entry.Contains('/', StringComparison.Ordinal)).Select(entry => entry.Split('/')[0]).Distinct(StringComparer.Ordinal).ToList();
    }

    // The row of the import report for the configuration of the key: "Configuration  <key>  <outcome>  <code>  <message>".
    private static string ConfigurationRowOf(SformResult import, string key)
    {
        return AnnotationOutput.LinesWith(import, key).FirstOrDefault(line => line.StartsWith("Configuration ", StringComparison.Ordinal))
            ?? throw import.Fail($"listed no configuration of {key}");
    }

    // The row of the import report for the type schema: "Type schema  rst  <outcome>  <code>  <message>".
    private static string TypeSchemaRowOf(SformResult import)
    {
        var rows = AnnotationOutput.LinesWith(import, "Type schema");
        return rows.Count > 0 ? rows[0] : throw import.Fail("listed no type schema");
    }

    // The rows of story config versions: the lines that start with a version number.
    private static int VersionRows(SformResult versions)
    {
        return versions.Lines.Count(line => VersionRowPattern().IsMatch(line.Trim()));
    }

    // The value of conf in the stored content of the key's configuration in the view of the acting project.
    private async Task<string?> ConfigurationValueAsync(string key, string view)
    {
        var content = (await Sform("story", "config", key, "-p", ProjectKey, "-v", view, "--content-only")).ShouldSucceed().JsonNode();
        return content["conf"]?.GetValue<string>();
    }

    [GeneratedRegex(@"Exported .+ (?:to|into) (?<path>\S+)\.$", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedPattern();

    [GeneratedRegex(@"The export \S+ is still (?:queued|running); it continues on the server\.", RegexOptions.CultureInvariant)]
    private static partial Regex StillRunningPattern();

    [GeneratedRegex(@"^templates/rst\.template\.\d+\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateVersionPattern();

    [GeneratedRegex(@"^\d+\s", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRowPattern();

    // What SeedViewAsync created: the keys and the current values of the configuration's conf and the template's tmpl.
    private sealed record SeededView(string Responsibility, string Unit, string Other, string ConfigurationValue, string TemplateValue);
}
