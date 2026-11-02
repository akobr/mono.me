using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Export & Import tab (…/modeling/export-import, pages/…/modeling/export-import.vue), Administrator and up:
// the cards "Export" and "Import" (regions named by their h2, side by side from lg) and, once the view's latest import
// succeeded, "Latest import". Both cards have the same four kind checkboxes, so scope them to their region. A
// Contributor gets the lock "Administrators only" instead, under the four other tabs with none selected; the tab
// itself is listed from Administrator up.
// - Export (ExportPanel.vue): the kinds (all checked; none is the field error "Pick at least one kind." and a disabled
//   Export), the format "Folder of JSON files" (default) or "Git repository", and "Export". The server builds the zip
//   in the background (the mock in about 2 seconds of polling), the app downloads it, the card says "<file> was
//   downloaded." and the toast "Exported <counts>" confirms it.
// - Import (ImportPanel.vue): a hidden file input (one file; a new one replaces it, listed with "Remove <file>"), the
//   kinds the import may write, "Existing documents" ("Overwrite", the default, or "Skip"), the force checkbox, and
//   "Check" (a dry run) or "Import". While it runs a status shows the progress; a refused upload or a failed job is the
//   alert "The import failed".
// - Latest import (ImportResult.vue): the banner ("Checked: nothing was written" or "Imported", the description
//   "<file>: <counts>." and, after a check, "Import now"), the summary per kind and in total (a table from md, 768 px,
//   a list below), the chips of the group "Filter the documents" with "Clear the filter", and the documents that are
//   not unchanged (the table "Documents" from md, the list "Documents" below), each with its kind and outcome badges,
//   its name (a link when the view has the document), path, code, message and warnings. It shows again after a reload.
// An import that ends while the page does not show it gets a toast "Imported <file>" with "Show the result".
public sealed partial class ModelingExportImportPage(App app, AppRoutes routes)
{
    // The description under "Administrators only".
    public const string AdministratorsOnlyText = "The export and the import of the view are available to the project's Administrators.";

    // The line under the export's format.
    public const string NeverIncludedText = "Never included: secrets, effective documents, the audit, the version history of annotations, and deleted annotations.";

    // The description of "Overwrite" under "Existing documents".
    public const string OverwriteDescriptionText = "The archive's content replaces them as a new version. Annotations are updated, and deleted ones are re-created with an empty configuration.";

    // The field error of the kinds, in both cards.
    public const string PickAtLeastOneKindText = "Pick at least one kind.";

    // The label of the import's force checkbox.
    public const string ForceLabel = "Store schemas and configurations that break each other";

    // The banner of a dry run and of a real run.
    public const string CheckedTitle = "Checked: nothing was written";

    public const string ImportedTitle = "Imported";

    // From this width the result's summary and documents are tables; below it lists.
    public const int ResultTableWidth = 768;

    // How long an export or an import may take: the mock ends a job after about 2 seconds of polling, a deployed API in
    // the background within minutes (the app gives up after 15).
    public const float TransferTimeoutMs = 180_000;

    public ILocator ExportRegion => app.Page.GetByRole(AriaRole.Region, new() { Name = "Export", Exact = true });

    public ILocator ImportRegion => app.Page.GetByRole(AriaRole.Region, new() { Name = "Import", Exact = true });

    public ILocator LatestImportRegion => app.Page.GetByRole(AriaRole.Region, new() { Name = "Latest import", Exact = true });

    // The lock of a Contributor.
    public ILocator AdministratorsOnly => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Administrators only", Exact = true });

    public ILocator AdministratorsOnlyDescription => app.Page.GetByText(AdministratorsOnlyText, new() { Exact = true });

    // --- Export ---
    public ILocator ExportButton => ExportRegion.GetByRole(AriaRole.Button, new() { Name = "Export", Exact = true });

    public ILocator ExportKindError => ExportRegion.GetByText(PickAtLeastOneKindText, new() { Exact = true });

    public ILocator NeverIncluded => ExportRegion.GetByText(NeverIncludedText, new() { Exact = true });

    // The progress of a running export, then "<file> was downloaded.".
    public ILocator ExportStatus => ExportRegion.GetByRole(AriaRole.Status);

    // --- Import ---

    // The hidden input of the drop zone; SetInputFilesAsync bypasses its accept filter.
    public ILocator ImportFileInput => ImportRegion.Locator("input[type=file]");

    public ILocator ForceCheckbox => ImportRegion.GetByRole(AriaRole.Checkbox, new() { Name = ForceLabel, Exact = true });

    public ILocator OverwriteDescription => ImportRegion.GetByText(OverwriteDescriptionText, new() { Exact = true });

    public ILocator CheckButton => ImportRegion.GetByRole(AriaRole.Button, new() { Name = "Check", Exact = true });

    public ILocator ImportButton => ImportRegion.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true });

    // The progress of a running import ("<n> of <total> files imported…" or "Waiting for the server…").
    public ILocator ImportProgress => ImportRegion.GetByRole(AriaRole.Status);

    // The title of the alert of a refused upload or a failed job; its description is the error.
    public ILocator ImportFailed => ImportRegion.GetByText("The import failed", new() { Exact = true });

    // --- Latest import ---
    public ILocator ImportNowButton => LatestImportRegion.GetByRole(AriaRole.Button, new() { Name = "Import now", Exact = true });

    public ILocator ClearFilterButton => LatestImportRegion.GetByRole(AriaRole.Button, new() { Name = "Clear the filter", Exact = true });

    // The result of a check whose documents all equal the stored ones (no documents are listed).
    public ILocator EveryDocumentEqual => LatestImportRegion.GetByText("Every document equals the stored one.", new() { Exact = true });

    // The documents of the result, in order: the table's rows with cells from md (not its header row), the list's own
    // items below (not the items of their "Warnings").
    public ILocator DocumentRows => HasResultTables
        ? LatestImportRegion.GetByRole(AriaRole.Table, new() { Name = "Documents", Exact = true }).GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell) })
        : LatestImportRegion.GetByRole(AriaRole.List, new() { Name = "Documents", Exact = true }).Locator(":scope > li");

    // The result shows tables (summary, documents) rather than lists.
    public bool HasResultTables => app.Page.ViewportSize is not { Width: < ResultTableWidth };

    // A kind checkbox of the export: "Annotations", "Schemas", "Templates", "Configurations".
    public ILocator ExportKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return ExportRegion.GetByRole(AriaRole.Checkbox, new() { Name = kind, Exact = true });
    }

    // A format radio of the export: "Folder of JSON files" or "Git repository" (the description is not in the name).
    public ILocator ExportFormat(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return ExportRegion.GetByRole(AriaRole.Radio, new() { Name = format, Exact = true });
    }

    // A kind checkbox of the import.
    public ILocator ImportKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return ImportRegion.GetByRole(AriaRole.Checkbox, new() { Name = kind, Exact = true });
    }

    // A radio of "Existing documents": "Overwrite" or "Skip".
    public ILocator ExistingDocuments(string choice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choice);
        return ImportRegion.GetByRole(AriaRole.Radio, new() { Name = choice, Exact = true });
    }

    // "Remove <file>" of the picked file: the sign that the input holds it.
    public ILocator PickedArchive(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return ImportRegion.GetByRole(AriaRole.Button, new() { Name = $"Remove {fileName}", Exact = true });
    }

    // The banner's title ("Checked: nothing was written", "Imported").
    public ILocator ResultTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return LatestImportRegion.GetByText(title, new() { Exact = true });
    }

    // The banner's description of an archive, "<file>: <counts>.", by the file name it starts with.
    public ILocator ResultDescription(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return LatestImportRegion.GetByText(new Regex($"^{Regex.Escape(fileName)}: "));
    }

    // The summary of a kind ("Annotations", "Schemas", "Templates", "Configurations") or "Total": the table's row (its
    // cells are the counts, then Excluded and Ignored files) from md, the list's item ("Annotations: 1 to create, 0
    // unchanged") below.
    public ILocator SummaryRow(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return HasResultTables
            ? LatestImportRegion.GetByRole(AriaRole.Table, new() { Name = "Summary", Exact = true }).GetByRole(AriaRole.Row)
                .Filter(new() { Has = app.Page.GetByRole(AriaRole.Rowheader, new() { Name = kind, Exact = true }) })
            : LatestImportRegion.GetByRole(AriaRole.List, new() { Name = "Summary", Exact = true }).GetByRole(AriaRole.Listitem)
                .Filter(new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(kind)}:") });
    }

    // The documents of the result named name (a key, a type code, "<type> below <key>"), and of the kind badge when
    // given ("Annotation", "Annotation schema", "Configuration", …): the annotation, its configuration and its schema
    // have the same name.
    public ILocator DocumentRow(string name, string? kind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var rows = DocumentRows.Filter(new() { Has = app.Page.GetByText(name, new() { Exact = true }) });
        return kind is null ? rows : rows.Filter(new() { Has = app.Page.GetByText(kind, new() { Exact = true }) });
    }

    // The link of a document's name, there when the view has the document.
    public ILocator DocumentLink(string name, string? kind = null)
    {
        return DocumentRow(name, kind).GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
    }

    // A chip of "Filter the documents" by its exact label, e.g. "Skipped 1", "Fails 1", "Warnings 2", "Annotations 2".
    public ILocator FilterChip(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return LatestImportRegion.GetByRole(AriaRole.Group, new() { Name = "Filter the documents", Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    // "Show the result" of the toaster's toast of an import (title "Imported <file>", "Checked <file>" or "The import
    // failed"), a link to this page.
    public ILocator ToastShowResult(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return app.Notifications.Locator("li")
            .Filter(new() { Has = app.Page.GetByText(title, new() { Exact = true }) })
            .Last
            .GetByRole(AriaRole.Link, new() { Name = "Show the result", Exact = true, IncludeHidden = true });
    }

    // Opens the tab by its URL and waits for the app; the page shows the cards or a lock.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.ModelingTab("export-import"));
        await app.WaitForReadyAsync();
    }

    // Opens the tab of an Administrator and waits for both cards.
    public async Task OpenCardsAsync()
    {
        await OpenAsync();
        await Expect(ExportRegion).ToBeVisibleAsync();
        await Expect(ImportRegion).ToBeVisibleAsync();
    }

    // Sets the archive on the import's input (replacing a picked one) and waits until the card lists it.
    public async Task ChooseArchiveAsync(FilePayload archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        await ImportFileInput.SetInputFilesAsync(archive);
        await Expect(PickedArchive(archive.Name)).ToBeVisibleAsync();
    }

    // Runs a check (dry run) of the picked archive and waits for its banner.
    public async Task CheckAsync(string fileName)
    {
        await Expect(CheckButton).ToBeEnabledAsync();
        await CheckButton.ClickAsync();
        await WaitForResultAsync(CheckedTitle, fileName);
    }

    // Imports the picked archive and waits for its banner.
    public async Task ImportAsync(string fileName)
    {
        await Expect(ImportButton).ToBeEnabledAsync();
        await ImportButton.ClickAsync();
        await WaitForResultAsync(ImportedTitle, fileName);
    }

    // "Import now" of a check's banner: the checked archive again, for real, with the check's options; waits for the
    // banner "Imported".
    public async Task ImportNowAsync(string fileName)
    {
        await ImportNowButton.ClickAsync();
        await WaitForResultAsync(ImportedTitle, fileName);
    }

    // Waits until "Latest import" shows the banner of the archive's run: the title and a description of its file.
    public async Task WaitForResultAsync(string title, string fileName)
    {
        await Expect(ResultTitle(title)).ToBeVisibleAsync(new() { Timeout = TransferTimeoutMs });
        await Expect(ResultDescription(fileName)).ToBeVisibleAsync(new() { Timeout = TransferTimeoutMs });
    }
}
