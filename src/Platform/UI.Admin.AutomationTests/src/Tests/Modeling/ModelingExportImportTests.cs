using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using TUnit.Assertions.Enums;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Modeling;

// Export and Import: the Export & Import tab of an Administrator. Exports of the target view are downloaded and read as
// zips (the kinds, at least one kind, the manifest export.json, the Git format); imports take zips the test builds in
// memory with its own new responsibilities only (a responsibility has no ancestors, so no AncestorCreated warning, and
// no template or type schema is written): Check, Import, Import now, Overwrite (the default) and Skip, force, refused
// and faulty files, the result with its summary, documents, chips and links, its restore after a reload, and the toast
// of an import that ended after the page was left.
// The tests of the class run one at a time among themselves, while the other classes go on in parallel: each starts
// view transfers (an export, a Check, an import) in the target project, and a deployed API refuses a new one with 409
// TransferLimit once the project has MaxActiveTransfers (3 by default) active (ViewTransferService.EnsureCapacityAsync,
// which counts a queued job for up to an hour); the mock has no such limit.
[Area(Area.Modeling)]
[NotInParallel(nameof(ModelingExportImportTests))]
public sealed partial class ModelingExportImportTests : PlatformTest
{
    private const string FolderFormat = "Folder of JSON files";
    private const string GitFormat = "Git repository";

    // Validates retries up to 1, so the configuration {"retries":5} of the same archive breaks it.
    private const string BreakingSchema = """{"type":"object","properties":{"retries":{"type":"integer","maximum":1}}}""";

    // The four kinds in their order, as the cards and export.json's Include list them.
    private static readonly string[] Kinds = ["Annotations", "Schemas", "Templates", "Configurations"];

    // MOD-EXI-01: Export the view as a folder of JSON files and check it back in.
    // An Administrator exports every kind of the view as a folder of JSON files: the download is named after the
    // organization, project, view and UTC time, its export.json names the view and the four kinds, and it holds the
    // test's new responsibility. Checked back in, the archive writes nothing and lists no document of the test's key,
    // because an unchanged document is not listed (on the mock every document is unchanged). Another test may change a
    // deployed view between the export and the check, so only the test's key is asserted there.
    [Test]
    [TestCase(151)]
    [UserRole(Role.Admin)]
    public async Task Export_FolderOfJsonFiles_ChecksBackInUnchanged()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        foreach (var kind in Kinds)
        {
            await Expect(transfer.ExportKind(kind)).ToBeCheckedAsync();
        }

        await Expect(transfer.ExportFormat(FolderFormat)).ToBeCheckedAsync();
        await Expect(transfer.NeverIncluded).ToBeVisibleAsync();

        var export = await ExportAsync(transfer);
        await Expect(transfer.ExportStatus).ToHaveTextAsync($"{export.SuggestedFilename} was downloaded.");
        await Assert.That(export.SuggestedFilename).Matches(ExportName(git: false));

        using (var zip = OpenArchive(export))
        {
            var manifest = Manifest(zip);
            await Assert.That(manifest["Organization"]?.GetValue<string>()).IsEqualTo(Routes.Organization);
            await Assert.That(manifest["Project"]?.GetValue<string>()).IsEqualTo(Routes.Project);
            await Assert.That(manifest["View"]?.GetValue<string>()).IsEqualTo(Routes.View);
            await Assert.That(Included(manifest)).IsEquivalentTo(Kinds, CollectionOrdering.Matching);
            await Assert.That(EntryNames(zip)).Contains($"annotations/{key}.json");
        }

        await transfer.ChooseArchiveAsync(ViewArchiveBuilder.Payload(export.SuggestedFilename, export.Content));
        await transfer.CheckAsync(export.SuggestedFilename);
        await Expect(transfer.LatestImportRegion).Not.ToContainTextAsync(key);
        if (Target.IsMock)
        {
            await Expect(transfer.EveryDocumentEqual).ToBeVisibleAsync();
        }
    }

    // MOD-EXI-02: Export only some kinds, require one kind, and export a Git repository.
    // Without Schemas and Configurations the export counts only annotations and templates, and the zip holds only those
    // folders besides export.json, whose Include names the two kinds. Without any kind the card asks for one and
    // disables Export. With every kind as a Git repository the name gets "-git-", and the zip is a repository
    // (.git/HEAD) without export.json. Nothing is created.
    [Test]
    [TestCase(153)]
    [UserRole(Role.Admin)]
    public async Task Export_KindsAndGitFormat_ShapeTheArchive()
    {
        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();

        await transfer.ExportKind("Schemas").SetCheckedAsync(false);
        await transfer.ExportKind("Configurations").SetCheckedAsync(false);
        var folder = await ExportAsync(transfer, AnnotationsAndTemplatesToast());
        await Assert.That(folder.SuggestedFilename).Matches(ExportName(git: false));
        using (var zip = OpenArchive(folder))
        {
            await Assert.That(Included(Manifest(zip))).IsEquivalentTo(new[] { "Annotations", "Templates" }, CollectionOrdering.Matching);
            await Assert.That(EntryNames(zip).Where(name => name != "export.json"))
                .All(name => name.StartsWith("annotations/", StringComparison.Ordinal) || name.StartsWith("templates/", StringComparison.Ordinal));
        }

        await transfer.ExportKind("Annotations").SetCheckedAsync(false);
        await transfer.ExportKind("Templates").SetCheckedAsync(false);
        await Expect(transfer.ExportKindError).ToBeVisibleAsync();
        await Expect(transfer.ExportButton).ToBeDisabledAsync();

        foreach (var kind in Kinds)
        {
            await transfer.ExportKind(kind).SetCheckedAsync(true);
        }

        await Expect(transfer.ExportKindError).ToBeHiddenAsync();
        await transfer.ExportFormat(GitFormat).CheckAsync();
        var git = await ExportAsync(transfer);
        await Assert.That(git.SuggestedFilename).Matches(ExportName(git: true));
        using (var zip = OpenArchive(git))
        {
            var names = EntryNames(zip);
            await Assert.That(names).Contains(".git/HEAD");
            await Assert.That(names).DoesNotContain("export.json");
        }
    }

    // MOD-EXI-03: Check an archive with a new annotation, then Import now.
    // A zip with a new responsibility and a file that is no document: the check would create the annotation and ignore
    // the other file, lists the annotation without a link (it does not exist yet), and offers Import now, which imports
    // the same file for real. The created annotation is linked, and its detail has the archive's title.
    [Test]
    [TestCase(155)]
    [UserRole(Role.Admin)]
    public async Task Import_CheckThenImportNow_CreatesAnnotation()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var title = $"AT import {PlatformData.RunSlug}";
        var fileName = $"{PlatformData.RunSlug}-import.zip";
        var archive = new ViewArchiveBuilder()
            .AddAnnotation(key, new { Title = title })
            .Add("notes.txt", $"Not a document of the view ({PlatformData.RunSlug}).")
            .ToPayload(fileName);

        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        await transfer.ChooseArchiveAsync(archive);
        await transfer.CheckAsync(fileName);
        await Expect(transfer.ResultDescription(fileName)).ToHaveTextAsync($"{fileName}: 1 to create, 0 unchanged.");

        await ExpectSummaryAsync(transfer, "Annotations", ["1", "0", "0", "0", "0", "0", string.Empty, string.Empty], "Annotations: 1 to create, 0 unchanged");
        await ExpectSummaryAsync(transfer, "Total", ["1", "0", "0", "0", "0", "0", "0", "1"], "Total: 1 to create, 0 unchanged, 0 excluded and 1 ignored files");

        var row = transfer.DocumentRow(key);
        await Expect(transfer.DocumentRows).ToHaveCountAsync(1);
        await Expect(row.GetByText("Annotation", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(row.GetByText("To create", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(transfer.DocumentLink(key)).ToHaveCountAsync(0);

        await transfer.ImportNowAsync(fileName);
        await Expect(transfer.ResultDescription(fileName)).ToHaveTextAsync($"{fileName}: 1 created, 0 unchanged.");
        await Expect(row.GetByText("Created", new() { Exact = true })).ToBeVisibleAsync();

        await transfer.DocumentLink(key).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        await Expect(new AnnotationDetail(App, Routes).Title).ToHaveValueAsync(title);
    }

    // MOD-EXI-04: Skip keeps an existing document, and the result shows again after a reload.
    // A first import creates a responsibility. A second archive with a different title for it and a new responsibility,
    // imported with Skip, creates only the new one and keeps the existing one (DocumentExists, linked). The chip
    // "Skipped 1" filters the documents to it. After a reload the result is still there, without Import now, and the
    // existing annotation still has the first title.
    [Test]
    [TestCase(156)]
    [UserRole(Role.Admin)]
    public async Task Import_SkipExisting_KeepsItAfterReload()
    {
        var existing = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var added = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var first = $"First {PlatformData.RunSlug}";
        var fileA = $"{PlatformData.RunSlug}-skip-a.zip";
        var fileB = $"{PlatformData.RunSlug}-skip-b.zip";

        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        await transfer.ChooseArchiveAsync(new ViewArchiveBuilder().AddAnnotation(existing, new { Title = first }).ToPayload(fileA));
        await transfer.ImportAsync(fileA);
        await Expect(transfer.DocumentRow(existing).GetByText("Created", new() { Exact = true })).ToBeVisibleAsync();

        await transfer.ChooseArchiveAsync(new ViewArchiveBuilder()
            .AddAnnotation(existing, new { Title = $"Second {PlatformData.RunSlug}" })
            .AddAnnotation(added, new { Title = $"New {PlatformData.RunSlug}" })
            .ToPayload(fileB));
        await transfer.ExistingDocuments("Skip").CheckAsync();
        await transfer.ImportAsync(fileB);
        await Expect(transfer.ResultDescription(fileB)).ToHaveTextAsync($"{fileB}: 1 created, 0 unchanged, 1 skipped.");

        await ExpectSkippedAsync(transfer, existing);

        var skipped = transfer.FilterChip("Skipped 1");
        await skipped.ClickAsync();
        await Expect(skipped).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(transfer.DocumentRows).ToHaveCountAsync(1);
        await Expect(transfer.DocumentRow(existing)).ToBeVisibleAsync();
        await transfer.ClearFilterButton.ClickAsync();
        await Expect(transfer.DocumentRows).ToHaveCountAsync(2);
        await Expect(transfer.DocumentRow(added)).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await transfer.WaitForResultAsync(ModelingExportImportPage.ImportedTitle, fileB);
        await ExpectSkippedAsync(transfer, existing);
        await Expect(transfer.ImportNowButton).ToHaveCountAsync(0);

        await transfer.DocumentLink(existing).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(existing));
        await Expect(new AnnotationDetail(App, Routes).Title).ToHaveValueAsync(first);
    }

    // MOD-EXI-05: A file that is not a zip is refused, and excluded, ignored and invalid files are reported.
    // A text named like a zip is refused at the upload ("The import failed"), after which Check and Import are enabled
    // again. A zip with a valid and an invalid annotation, a configuration and a file that is no document, checked
    // without Configurations, would create one annotation and fail the other (InvalidDocument), and counts one excluded
    // and one ignored file; the chip "Fails 1" filters to the invalid one. Nothing is written: one upload is refused,
    // the other is a dry run.
    [Test]
    [TestCase(171)]
    [UserRole(Role.Admin)]
    public async Task Import_FaultyArchive_ReportsEveryProblem()
    {
        var valid = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var invalid = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var fileName = $"{PlatformData.RunSlug}-faulty.zip";
        var archive = new ViewArchiveBuilder()
            .AddAnnotation(valid, new { Title = $"A {PlatformData.RunSlug}" })
            .AddConfiguration(valid, """{"retries":1}""")
            .Add($"annotations/{invalid}.json", "{ not json")
            .Add("notes.txt", $"Not a document of the view ({PlatformData.RunSlug}).")
            .ToPayload(fileName);

        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        await transfer.ChooseArchiveAsync(ViewArchiveBuilder.NotAZip("not-a-zip.zip", "not a zip"));
        await transfer.ImportButton.ClickAsync();
        await Expect(transfer.ImportFailed).ToBeVisibleAsync(new() { Timeout = ModelingExportImportPage.TransferTimeoutMs });
        if (Target.IsMock)
        {
            // A deployed API refuses it as InvalidArchive in its own words.
            await Expect(transfer.ImportRegion.GetByText("The body is not a zip archive.", new() { Exact = true })).ToBeVisibleAsync();
        }

        await Expect(transfer.CheckButton).ToBeEnabledAsync();
        await Expect(transfer.ImportButton).ToBeEnabledAsync();

        await transfer.ChooseArchiveAsync(archive);
        await transfer.ImportKind("Configurations").SetCheckedAsync(false);
        await transfer.CheckAsync(fileName);
        await Expect(transfer.ResultDescription(fileName)).ToHaveTextAsync($"{fileName}: 1 to create, 0 unchanged, 1 failed.");

        await Expect(transfer.SummaryRow("Configurations")).ToHaveCountAsync(0);
        await ExpectSummaryAsync(transfer, "Annotations", ["1", "0", "0", "0", "1", "0", string.Empty, string.Empty], "Annotations: 1 to create, 0 unchanged, 1 failed");
        await ExpectSummaryAsync(transfer, "Total", ["1", "0", "0", "0", "1", "0", "1", "1"], "Total: 1 to create, 0 unchanged, 1 failed, 1 excluded and 1 ignored files");

        // The message of an invalid document is the parser's, which differs between the mock and the API.
        var failed = transfer.DocumentRow(invalid);
        await Expect(failed.GetByText("Fails", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(failed.GetByText("InvalidDocument", new() { Exact = true })).ToBeVisibleAsync();

        var fails = transfer.FilterChip("Fails 1");
        await fails.ClickAsync();
        await Expect(fails).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(transfer.DocumentRows).ToHaveCountAsync(1);
        await Expect(failed).ToBeVisibleAsync();
        await transfer.ClearFilterButton.ClickAsync();
        await Expect(transfer.DocumentRows).ToHaveCountAsync(2);
        await Expect(transfer.DocumentRow(valid)).ToBeVisibleAsync();
    }

    // MOD-EXI-06: A schema that breaks a configuration fails the import unless forced.
    // An archive with an annotation, its configuration (retries 5) and an annotation schema that allows at most 1:
    // without force the schema fails (SchemaViolation) and the rest is imported, so no schema is stored. With "Store
    // schemas and configurations that break each other" a second such archive stores the schema with a SchemaViolation
    // warning (the forced configuration warns too: "Warnings 2"), and its link opens the stored schema. Only the test's
    // own keys are written.
    [Test]
    [TestCase(172)]
    [UserRole(Role.Admin)]
    public async Task Import_BreakingSchema_FailsUnlessForced()
    {
        var refused = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var forced = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var fileX = $"{PlatformData.RunSlug}-force-x.zip";
        var fileY = $"{PlatformData.RunSlug}-force-y.zip";

        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        await transfer.ChooseArchiveAsync(BreakingArchive(refused).ToPayload(fileX));
        await transfer.ImportAsync(fileX);

        var refusedSchema = transfer.DocumentRow(refused, "Annotation schema");
        await Expect(refusedSchema.GetByText("Failed", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(refusedSchema.GetByText("SchemaViolation", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(refusedSchema).ToContainTextAsync($"The configuration of {refused} violates this schema.");
        await Expect(transfer.DocumentRow(refused, "Configuration").GetByText("Created", new() { Exact = true })).ToBeVisibleAsync();

        await transfer.ForceCheckbox.CheckAsync();
        await transfer.ChooseArchiveAsync(BreakingArchive(forced).ToPayload(fileY));
        await transfer.ImportAsync(fileY);

        var forcedSchema = transfer.DocumentRow(forced, "Annotation schema");
        await Expect(forcedSchema.GetByText("Created", new() { Exact = true })).ToBeVisibleAsync();
        var warning = forcedSchema.GetByRole(AriaRole.List, new() { Name = "Warnings", Exact = true });
        await Expect(warning).ToContainTextAsync("SchemaViolation");
        await Expect(warning).ToContainTextAsync($"Force stores this schema although the configuration of {forced} violates it.");
        await Expect(transfer.FilterChip("Warnings 2")).ToBeVisibleAsync();

        var schema = new ModelingSchemaPage(App, Routes);
        await transfer.DocumentLink(forced, "Annotation schema").ClickAsync();
        await App.ExpectAtAsync(Routes.AnnotationSchema(forced));
        await Expect(schema.Document.Content).ToContainTextAsync(MaximumOne());

        await schema.OpenAnnotationAsync(refused);
        await schema.Document.ExpectTextAsync(ModelingSchemaPage.StarterDocument);
        await Expect(schema.CreateButton).ToBeVisibleAsync();
    }

    // MOD-EXI-07: The toaster reports an import that ended after leaving the page.
    // The import is started and the page left for Annotations while it runs. An import the page does not show gets a
    // toast when it ends, "Imported <file>" with its counts, whose "Show the result" returns to the page with the
    // result. Had the import ended before the page was left, there would be no toast, and the test fails.
    [Test]
    [TestCase(173)]
    [UserRole(Role.Admin)]
    public async Task Import_EndedElsewhere_ToastShowsResult()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var fileName = $"{PlatformData.RunSlug}-toast.zip";
        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        await transfer.ChooseArchiveAsync(new ViewArchiveBuilder().AddAnnotation(key, new { Title = $"AT toast {PlatformData.RunSlug}" }).ToPayload(fileName));

        // The progress shows once the upload went through; the mock's job ends about 2 seconds later.
        await transfer.ImportButton.ClickAsync();
        await Expect(transfer.ImportProgress).ToBeVisibleAsync();
        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        await Expect(new AnnotationsPage(App, Routes).Filter).ToBeVisibleAsync();

        var title = $"Imported {fileName}";
        await Expect(App.Toast(title)).ToBeVisibleAsync(new() { Timeout = ModelingExportImportPage.TransferTimeoutMs });
        await Expect(App.ToastDescriptionOf(title)).ToHaveTextAsync("1 created, 0 unchanged");

        await transfer.ToastShowResult(title).ClickAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("export-import"));
        await transfer.WaitForResultAsync(ModelingExportImportPage.ImportedTitle, fileName);
        await Expect(transfer.DocumentRow(key).GetByText("Created", new() { Exact = true })).ToBeVisibleAsync();
    }

    // MOD-EXI-08: Overwrite, the default, updates an existing document.
    // "Existing documents" starts with Overwrite. A first archive creates a responsibility; a second one with another
    // title for it, checked with the default, would update it: the check links the existing annotation and offers
    // Import now, whose real run updates it (had the check written, the run would find nothing to change). The detail
    // shows the second title.
    [Test]
    [TestCase(175)]
    [UserRole(Role.Admin)]
    public async Task Import_OverwriteExisting_UpdatesDocument()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var second = $"Second {PlatformData.RunSlug}";
        var fileA = $"{PlatformData.RunSlug}-overwrite-a.zip";
        var fileB = $"{PlatformData.RunSlug}-overwrite-b.zip";

        var transfer = new ModelingExportImportPage(App, Routes);
        await transfer.OpenCardsAsync();
        await Expect(transfer.ExistingDocuments("Overwrite")).ToBeCheckedAsync();
        await Expect(transfer.ExistingDocuments("Skip")).Not.ToBeCheckedAsync();
        await Expect(transfer.OverwriteDescription).ToBeVisibleAsync();

        await transfer.ChooseArchiveAsync(new ViewArchiveBuilder().AddAnnotation(key, new { Title = $"First {PlatformData.RunSlug}" }).ToPayload(fileA));
        await transfer.ImportAsync(fileA);
        await Expect(transfer.ResultDescription(fileA)).ToHaveTextAsync($"{fileA}: 1 created, 0 unchanged.");
        await Expect(transfer.DocumentRow(key).GetByText("Created", new() { Exact = true })).ToBeVisibleAsync();

        await transfer.ChooseArchiveAsync(new ViewArchiveBuilder().AddAnnotation(key, new { Title = second }).ToPayload(fileB));
        await transfer.CheckAsync(fileB);
        await Expect(transfer.ResultDescription(fileB)).ToHaveTextAsync($"{fileB}: 1 to update, 0 unchanged.");
        await Expect(transfer.ImportNowButton).ToBeVisibleAsync();

        await ExpectSummaryAsync(transfer, "Annotations", ["0", "1", "0", "0", "0", "0", string.Empty, string.Empty], "Annotations: 1 to update, 0 unchanged");
        await ExpectSummaryAsync(transfer, "Total", ["0", "1", "0", "0", "0", "0", "0", "0"], "Total: 1 to update, 0 unchanged, 0 excluded and 0 ignored files");

        var row = transfer.DocumentRow(key);
        await Expect(row.GetByText("Annotation", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(row.GetByText("To update", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(transfer.DocumentLink(key)).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Routes.Annotation(key)));

        await transfer.ImportNowAsync(fileB);
        await Expect(transfer.ResultDescription(fileB)).ToHaveTextAsync($"{fileB}: 1 updated, 0 unchanged.");
        await Expect(row.GetByText("Updated", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(transfer.ImportNowButton).ToHaveCountAsync(0);

        await transfer.DocumentLink(key).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        await Expect(new AnnotationDetail(App, Routes).Title).ToHaveValueAsync(second);
    }

    private static ZipArchive OpenArchive(DownloadedFile file)
    {
        return new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
    }

    private static string[] EntryNames(ZipArchive zip)
    {
        return zip.Entries.Select(entry => entry.FullName).ToArray();
    }

    // export.json of a folder export.
    private static JsonNode Manifest(ZipArchive zip)
    {
        var entry = zip.GetEntry("export.json") ?? throw new InvalidOperationException("The export has no export.json.");
        using var stream = entry.Open();
        return JsonNode.Parse(stream) ?? throw new InvalidOperationException("export.json is empty.");
    }

    // The kinds export.json's Include names, in its order.
    private static string[] Included(JsonNode manifest)
    {
        return manifest["Include"]?.AsArray().Select(kind => kind?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
    }

    // The toast of an export of everything but schemas and configurations; a deployed view may have no template.
    [GeneratedRegex(@"^Exported \d+ annotations? and \d+ templates?$")]
    private static partial Regex AnnotationsAndTemplatesToast();

    // The toast of every finished export: "Exported <counts>".
    [GeneratedRegex("^Exported ")]
    private static partial Regex ExportedToast();

    [GeneratedRegex(@"""maximum"":\s*1\b")]
    private static partial Regex MaximumOne();

    // An annotation of the key, its configuration {"retries":5} and an annotation schema that it breaks.
    private ViewArchiveBuilder BreakingArchive(string key)
    {
        return new ViewArchiveBuilder()
            .AddAnnotation(key, new { Title = $"AT force {PlatformData.RunSlug}" })
            .AddConfiguration(key, """{"retries":5}""")
            .AddAnnotationSchema(key, BreakingSchema);
    }

    // export-<org>-<project>-<view>[-git]-<yyyyMMdd>-<HHmmss>Z.zip of the target view (UTC).
    private Regex ExportName(bool git)
    {
        var view = $"{Regex.Escape(Routes.Organization)}-{Regex.Escape(Routes.Project)}-{Regex.Escape(Routes.View)}";
        return new Regex($@"^export-{view}{(git ? "-git" : string.Empty)}-\d{{8}}-\d{{6}}Z\.zip$");
    }

    // Exports with the card's kinds and format and returns the downloaded zip, after the toast that confirms it. The
    // toast lasts about 5 seconds, so it is asserted as soon as the download starts, before the zip is read; it is the
    // one whose description is the downloaded file's name, because the toast of an export a moment before can still
    // show.
    private async Task<DownloadedFile> ExportAsync(ModelingExportImportPage transfer, Regex? toast = null)
    {
        await Expect(transfer.ExportButton).ToBeEnabledAsync();
        return await DownloadAsync(
            () => transfer.ExportButton.ClickAsync(),
            async fileName =>
            {
                var exported = App.ToastItem(fileName);
                await Expect(exported.GetByText(toast ?? ExportedToast())).ToBeVisibleAsync();
            },
            (int)ModelingExportImportPage.TransferTimeoutMs);
    }

    // The summary of a kind or of "Total": the table row's cells (the counts, Excluded files, Ignored files) from md, the
    // list's line below.
    private static async Task ExpectSummaryAsync(ModelingExportImportPage transfer, string kind, string[] cells, string line)
    {
        if (transfer.HasResultTables)
        {
            await Expect(transfer.SummaryRow(kind).GetByRole(AriaRole.Cell)).ToHaveTextAsync(cells);
        }
        else
        {
            await Expect(transfer.SummaryRow(kind)).ToHaveTextAsync(line);
        }
    }

    // The row of an annotation that Skip kept: Skipped, DocumentExists with its message, and linked.
    private static async Task ExpectSkippedAsync(ModelingExportImportPage transfer, string key)
    {
        var row = transfer.DocumentRow(key);
        await Expect(row.GetByText("Skipped", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(row.GetByText("DocumentExists", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync($"The view has a different annotation {key}, and Skip keeps it.");
        await Expect(transfer.DocumentLink(key)).ToBeVisibleAsync();
    }
}
