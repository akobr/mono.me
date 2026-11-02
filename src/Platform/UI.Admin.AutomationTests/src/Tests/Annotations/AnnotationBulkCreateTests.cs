using System.Globalization;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Bulk Create: the dialog "Bulk create annotations", its review and its result.
[Area(Area.Annotations)]
public sealed partial class AnnotationBulkCreateTests : PlatformTest
{
    // ANN-BLK-01: Bulk create a responsibility and find it in the grid.
    // The loaded page shows the grid (the list on a phone) and the filter. Bulk create (the toolbar on a wide screen,
    // the command palette on a phone) creates a new responsibility, whose result replaces the form without a toast; the
    // grid filtered by its key shows it, and its detail, opened by the URL, names it. Both are virtualized, so the test
    // looks at the filtered result only.
    [Test]
    [Elementary]
    [TestCase(2)]
    [UserRole(Role.Editor)]
    public async Task Annotations_CreateResponsibility_AppearsInGrid()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        // An empty view (a fresh local target) shows its empty state instead of the grid.
        var items = Page.ViewportSize is { Width: < AnnotationsPage.GridWidth } ? annotations.AnnotationList : annotations.AnnotationGrid;
        await Expect(items.Or(annotations.EmptyState)).ToBeVisibleAsync();
        await Expect(annotations.Filter).ToBeVisibleAsync();

        var bulkCreate = await annotations.OpenBulkCreateAsync();
        await bulkCreate.EnterKeysAsync(key);
        await Expect(bulkCreate.Summary).ToHaveTextAsync("1 new");
        await Expect(bulkCreate.ReviewRow(key)).ToContainTextAsync("New");
        await bulkCreate.CreateAsync(1);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 1 annotation.");
        await Expect(bulkCreate.Keys).ToHaveCountAsync(0);
        await Expect(App.Notifications.Locator("li")).ToHaveCountAsync(0);
        await bulkCreate.CloseAsync();

        await annotations.FilterAsync($"key:{key}");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();

        var detail = new AnnotationDetail(App, Routes);
        await detail.OpenAsync(key);
        await Expect(detail.Identity).ToHaveTextAsync(key);
    }

    // ANN-BLK-02: Compact lines create their missing ancestors.
    // A typed execution and a compact line (two parts, a usage) are reviewed as two new keys with five missing
    // ancestors; the result lists all seven, the list's keys "From the list" and the rest "Missing ancestor", and
    // "Create more" empties the input for the next list. The subject's filter then counts the five keys of the subject,
    // and a bulk-created key has no title and no time zone of its own.
    [Test]
    [TestCase(18)]
    [UserRole(Role.Editor)]
    public async Task BulkCreate_CompactLines_CreatesMissingAncestors()
    {
        var execution = PlatformData.AnnotationKey(PlatformData.Execution);
        var subject = execution.Split('.')[1];
        var compact = $"{subject}.{PlatformData.AnnotationPart()}";
        var usage = $"{PlatformData.Usage}.{compact}";
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        var bulkCreate = await annotations.OpenBulkCreateAsync();
        await Expect(bulkCreate.Root).ToHaveAccessibleDescriptionAsync($"In {Routes.Organization}/{Routes.Project}, view {Routes.View}.");

        await bulkCreate.EnterKeysAsync(execution, compact);
        await Expect(bulkCreate.Summary).ToHaveTextAsync("2 new · 5 missing ancestors");
        var line = bulkCreate.ReviewLine(2);
        await Expect(line.GetByText(usage, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(line.GetByText(compact, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(line).ToContainTextAsync("New");

        await bulkCreate.CreateAsync(2);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 7 annotations: 2 from the list and 5 missing ancestors.");
        await Expect(bulkCreate.CreatedRow(execution)).ToContainTextAsync("From the list");
        await Expect(bulkCreate.CreatedRow(usage)).ToContainTextAsync("From the list");
        string[] ancestors =
        [
            PlatformData.AncestorKey(execution, PlatformData.Usage),
            PlatformData.AncestorKey(execution, PlatformData.Context),
            PlatformData.AncestorKey(execution, PlatformData.Responsibility),
            PlatformData.AncestorKey(usage, PlatformData.Responsibility),
            PlatformData.AncestorKey(execution, PlatformData.Subject),
        ];
        foreach (var ancestor in ancestors)
        {
            await Expect(bulkCreate.CreatedRow(ancestor)).ToContainTextAsync("Missing ancestor");
        }

        // Seven created keys and the header row.
        await Expect(bulkCreate.CreatedAnnotations.GetByRole(AriaRole.Row)).ToHaveCountAsync(8);

        await bulkCreate.CreateMoreButton.ClickAsync();
        await Expect(bulkCreate.Result).ToBeHiddenAsync();
        await Expect(bulkCreate.Keys).ToHaveValueAsync(string.Empty);
        await Expect(bulkCreate.Keys).ToBeFocusedAsync();

        await bulkCreate.CancelAsync();
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync($"subject:{subject}");
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(5));

        var detail = new AnnotationDetail(App, Routes);
        await detail.OpenAsync(execution);
        await Expect(detail.Identity).ToHaveTextAsync(execution);
        await Expect(detail.Title).ToHaveValueAsync(string.Empty);
        await Expect(detail.TimeZoneButton).ToHaveTextAsync(BrowserTimeZone());
    }

    // ANN-BLK-03: Review marks existing, duplicate and invalid lines and enforces the limit.
    // 1,001 lines exceed the limit of 1,000 and disable the create. A list with an existing key, a repeated key and five
    // invalid lines is reviewed line by line, each invalid one with its problem; it cannot be created until "Remove
    // invalid lines" leaves the three valid lines, and then only the new key is written, the existing and the repeated
    // ones counted in the result's description.
    [Test]
    [TestCase(64)]
    [UserRole(Role.Editor)]
    public async Task BulkCreate_ReviewProblems_BlockUntilRemoved()
    {
        var existing = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var subject = PlatformData.AnnotationPart();
        var created = $"{PlatformData.Subject}.{subject}";
        var a = PlatformData.AnnotationPart();
        var b = PlatformData.AnnotationPart();
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(existing);
        await annotations.WaitForCatalogAsync();

        var bulkCreate = await annotations.OpenBulkCreateAsync();
        var tooMany = Enumerable.Range(1, 1001).Select(index => string.Create(CultureInfo.InvariantCulture, $"{created}-{index}")).ToArray();
        await bulkCreate.EnterKeysAsync(tooMany);
        await Expect(bulkCreate.KeysError).ToHaveTextAsync("At most 1,000 keys at once; the list has 1,001.");
        await Expect(bulkCreate.CreateButton(1001)).ToBeDisabledAsync();

        await bulkCreate.EnterKeysAsync(existing, created, created, $"exe.{a}.{b}", $"{a}..{b}", $"{a}/{b}", $"{a}\t{b}", $"{a}.{b}.{a}.{b}.{a}");
        await Expect(bulkCreate.Summary).ToHaveTextAsync("1 new · 1 already exists · 1 duplicate · 5 invalid");
        await Expect(bulkCreate.ReviewLine(1)).ToContainTextAsync("Exists");
        await Expect(bulkCreate.ReviewLine(2)).ToContainTextAsync("New");
        await Expect(bulkCreate.ReviewLine(3)).ToContainTextAsync("Duplicate");
        string[] problems =
        [
            "An Execution key has 4 parts: exe.subject.responsibility.context",
            "Has an empty part: two dots in a row, or a dot at the start or the end.",
            "Contains \"/\", which a name cannot hold.",
            "Contains a tab: cells copied from a spreadsheet? Join them with dots.",
            "Has 5 parts: a line without a type code has at most 4 (subject.responsibility.context.unit).",
        ];
        for (var index = 0; index < problems.Length; index++)
        {
            var invalid = bulkCreate.ReviewLine(index + 4);
            await Expect(invalid).ToContainTextAsync("Invalid");
            await Expect(invalid).ToContainTextAsync(problems[index]);
        }

        await Expect(bulkCreate.CreateButton(1)).ToBeDisabledAsync();
        await Expect(bulkCreate.RemoveInvalidLinesButton).ToBeVisibleAsync();

        await bulkCreate.RemoveInvalidLinesButton.ClickAsync();
        await Expect(bulkCreate.Keys).ToHaveValueAsync(string.Join('\n', existing, created, created));
        await Expect(bulkCreate.Summary).ToHaveTextAsync("1 new · 1 already exists · 1 duplicate");
        await Expect(bulkCreate.RemoveInvalidLinesButton).ToBeHiddenAsync();

        await bulkCreate.CreateAsync(1);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 1 annotation.");
        await Expect(bulkCreate.ResultDescription).ToHaveTextAsync("1 key already existed and was skipped. 1 repeated line was left out.");
        await Expect(bulkCreate.CreatedRow(created)).ToContainTextAsync("From the list");
    }

    // ANN-BLK-04: Bulk create over a deleted key offers Restore.
    // A deleted key is not in the catalog, so the review counts it as new; the create looks it up first and asks
    // "Deleted annotations". Cancel writes nothing (only the lookup was sent) and keeps the list; Restore brings the key
    // back instead of creating it, which the result says, the grid lists, and the key's history records.
    [Test]
    [TestCase(67)]
    [UserRole(Role.Editor)]
    public async Task BulkCreate_DeletedKey_OffersRestore()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        await InstallRequestRecorderAsync();
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var user = await App.SignedInNameAsync();
        await annotations.BulkCreateAsync(key);

        var detail = new AnnotationDetail(App, Routes);
        await detail.OpenAsync(key);
        await Expect(detail.Identity).ToHaveTextAsync(key);
        await detail.DeleteAsync(key);

        var bulkCreate = await annotations.OpenBulkCreateAsync();
        await bulkCreate.EnterKeysAsync(key);
        await Expect(bulkCreate.Summary).ToHaveTextAsync("1 new");
        await Expect(bulkCreate.ReviewRow(key)).ToContainTextAsync("New");

        await ClearRecordedRequestsAsync();
        var deleted = await bulkCreate.CreateOverDeletedAsync(1);
        await Expect(deleted.Item(key)).ToContainTextAsync(new Regex($@"Deleted .+ by\s+{Regex.Escape(user)}"));
        await Expect(deleted.CancelButton).ToBeVisibleAsync();
        await Expect(deleted.CreateNewButton).ToBeVisibleAsync();
        await Expect(deleted.Button(DeletedKeysDialog.RestoreLabel)).ToBeVisibleAsync();

        await deleted.CancelAsync();
        await Expect(bulkCreate.Keys).ToHaveValueAsync(key);
        await Expect(bulkCreate.CreateButton(1)).ToBeVisibleAsync();
        await ExpectRequestAsync(IsDeletedKeyLookup, "the lookup of deleted keys (POST …/annotations/deleted/lookup)");
        await ExpectNoRequestAsync(request => IsBulkCreate(request) || IsDeletedKeyRestore(request), "a bulk create (…/annotations/simple) or a restore (…/annotations/deleted/restore) after Cancel");

        deleted = await bulkCreate.CreateOverDeletedAsync(1);
        await deleted.RestoreAsync(DeletedKeysDialog.RestoreLabel);
        await Expect(bulkCreate.Result).ToHaveTextAsync("No annotations were created.");
        await Expect(bulkCreate.ResultDescription).ToHaveTextAsync("1 deleted annotation was restored.");

        await bulkCreate.CloseAsync();
        await annotations.FilterAsync($"key:{key}");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();

        var history = new AnnotationHistory(App);
        await App.GotoAsync(Routes.AnnotationHistory(key));
        await history.WaitForEntryAsync(Routes.AnnotationHistory(key), "Restored");
        await Expect(AnnotationHistory.ActionBadge(history.Region, "Deleted")).ToBeVisibleAsync();
    }

    // The placeholder of an empty time zone, "Browser time zone (<zone>)": the zone of the browser differs by profile.
    [GeneratedRegex(@"^Browser time zone \(")]
    private static partial Regex BrowserTimeZone();
}
