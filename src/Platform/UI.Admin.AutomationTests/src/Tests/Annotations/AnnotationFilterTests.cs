using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Filtering: the filter language of the catalog, its chips, count and notices, and the phone's filter sheet.
[Area(Area.Annotations)]
public sealed partial class AnnotationFilterTests : PlatformTest
{
    // ANN-FLT-01: Filter qualifiers, negation, OR, chips and count.
    // On nine own keys (a unit of execution and a second execution of the same subject and responsibility, with their
    // ancestors) every qualifier narrows the catalog to exactly the keys it names: subject, type with a negated OR,
    // key glob, ancestor, responsibility, context, unit and free text. The chips name the terms, a chip's remove button
    // drops its term from the box, and Clear empties the box and the URL.
    [Test]
    [TestCase(110)]
    [UserRole(Role.Editor)]
    public async Task AnnotationFilter_Qualifiers_NarrowTheCatalog()
    {
        var execution = PlatformData.AnnotationKey(PlatformData.Execution);
        var parts = execution.Split('.');
        var (subject, responsibility, secondContext) = (parts[1], parts[2], parts[3]);
        var unitOfExecution = PlatformData.AnnotationKey(PlatformData.UnitOfExecution, subject, responsibility);
        var uxeParts = unitOfExecution.Split('.');
        var (firstContext, unit) = (uxeParts[3], uxeParts[4]);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(unitOfExecution, execution);
        await annotations.WaitForCatalogAsync();

        await annotations.FilterAsync($"subject:{subject}");
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(7));
        await Expect(annotations.FilterChip($"subject:{subject}")).ToHaveTextAsync($"subject: {subject}");

        await annotations.FilterAsync($"subject:{subject} type:exe");
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(2));
        await Expect(annotations.FilterChip($"subject:{subject}")).ToHaveTextAsync($"subject: {subject}");
        await Expect(annotations.FilterChip("type:exe")).ToHaveTextAsync("type: exe");

        await annotations.FilterAsync($"subject:{subject} -type:exe,cnt");
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(3));
        await Expect(annotations.FilterChip("-type:exe,cnt")).ToHaveTextAsync("not type: exe or cnt");
        await ExpectRowsAsync(
            annotations,
            PlatformData.AncestorKey(execution, PlatformData.Subject),
            PlatformData.AncestorKey(execution, PlatformData.Usage),
            unitOfExecution);

        var firstExecution = PlatformData.AncestorKey(unitOfExecution, PlatformData.Execution);
        await annotations.FilterAsync($"key:exe.{subject}.*");
        await ExpectRowsAsync(annotations, firstExecution, execution);
        await annotations.FilterAsync($"ancestor:{PlatformData.AncestorKey(execution, PlatformData.Usage)}");
        await ExpectRowsAsync(annotations, firstExecution, execution, unitOfExecution);

        await annotations.FilterAsync($"responsibility:{responsibility}");
        await ExpectRowsAsync(
            annotations,
            PlatformData.AncestorKey(execution, PlatformData.Responsibility),
            PlatformData.AncestorKey(unitOfExecution, PlatformData.Unit),
            PlatformData.AncestorKey(execution, PlatformData.Usage),
            firstExecution,
            execution,
            unitOfExecution);
        await annotations.FilterAsync($"context:{firstContext}");
        await ExpectRowsAsync(annotations, PlatformData.AncestorKey(unitOfExecution, PlatformData.Context), firstExecution, unitOfExecution);
        await annotations.FilterAsync($"unit:{unit}");
        await ExpectRowsAsync(annotations, PlatformData.AncestorKey(unitOfExecution, PlatformData.Unit), unitOfExecution);

        await annotations.FilterAsync(secondContext);
        await ExpectRowsAsync(annotations, PlatformData.AncestorKey(execution, PlatformData.Context), execution);

        await annotations.FilterAsync($"subject:{subject} type:exe");
        await annotations.RemoveFilterTermButton("type:exe").ClickAsync();
        await Expect(annotations.Filter).ToHaveValueAsync($"subject:{subject}");
        await App.ExpectQueryAsync("q", $"subject:{subject}");
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(7));

        await annotations.ClearFilterButton.ClickAsync();
        await Expect(annotations.Filter).ToHaveValueAsync(string.Empty);
        await App.ExpectQueryAsync("q", null);
        await Expect(annotations.FilterCount).ToHaveTextAsync(TotalOnly());
    }

    // ANN-FLT-02: Invalid filter terms warn and are ignored; the syntax examples fill the box.
    // A term with an unknown type, status or attribute, or without a value, shows its problem next to the chips and
    // filters nothing, so the count is the total of the total. An unknown qualifier searches its text and finds
    // nothing. The "Filter syntax" popover lists the qualifiers, and its example fills the box and the URL.
    [Test]
    [TestCase(111)]
    public async Task AnnotationFilter_InvalidTerms_WarnAndAreIgnored()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        await annotations.FilterAsync("type:foo");
        await Expect(annotations.FilterNotice("Unknown type foo.")).ToBeVisibleAsync();
        await Expect(annotations.FilterCount).ToHaveTextAsync(TotalOfTotal());

        await annotations.FilterAsync("is:gone");
        await Expect(annotations.FilterNotice("is: accepts active, disabled, scheduled, expired.")).ToBeVisibleAsync();
        await annotations.FilterAsync("has:x");
        await Expect(annotations.FilterNotice("has: accepts config, labels, values, docs, description.")).ToBeVisibleAsync();
        await annotations.FilterAsync("type:");
        await Expect(annotations.FilterNotice("type: needs a value.")).ToBeVisibleAsync();

        await annotations.FilterAsync($"zz:{PlatformData.AnnotationPart()}");
        await Expect(annotations.FilterNotice("Unknown qualifier zz: (searched as text).")).ToBeVisibleAsync();
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(0));

        const string example = "key:exe.*.payments.*";
        await annotations.FilterSyntaxButton.ClickAsync();
        await Expect(annotations.FilterSyntaxPopover).ToBeVisibleAsync();
        await Expect(annotations.FilterSyntaxQualifiers).ToHaveTextAsync(
            "Qualifiers: type, label, is, has, key, subject, responsibility, context, unit, ancestor. A comma means OR, - negates, quotes keep spaces; other words search name, key, title and description.");
        await annotations.FilterSyntaxExample(example).ClickAsync();
        await Expect(annotations.Filter).ToHaveValueAsync(example);
        await App.ExpectQueryAsync("q", example);
    }

    // ANN-FLT-03: Filters bottom sheet on a phone.
    // The sheet's toggles write the terms of a type, a status and the configuration into the filter, pressed while
    // their term is in it and listed under "Active filter"; removing a term there releases its toggle. Done applies
    // the box's text to the URL and shows the number of terms on "Filters", and "Clear all" empties both.
    [Test]
    [TestCase(112)]
    [PhoneOnly]
    public async Task AnnotationFilter_PhoneSheet_BuildsTheQuery()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var sheet = annotations.FilterSheet;

        await sheet.OpenAsync();
        await Expect(sheet.Root).ToHaveAccessibleDescriptionAsync("Narrow the annotations of this view.");

        string[] toggles = ["Executions", "Active", "Has a configuration"];
        foreach (var toggle in toggles)
        {
            await sheet.Toggle(toggle).ClickAsync();
            await Expect(sheet.Toggle(toggle)).ToHaveAttributeAsync("aria-pressed", "true");
        }

        await Expect(sheet.ActiveFilterTerms).ToHaveTextAsync(["type:exe", "is:active", "has:config"]);

        await sheet.ActiveFilterTerm("is:active").ClickAsync();
        await Expect(sheet.ActiveFilterTerms).ToHaveTextAsync(["type:exe", "has:config"]);
        await Expect(sheet.Toggle("Active")).ToHaveAttributeAsync("aria-pressed", "false");

        await sheet.DoneAsync();
        await App.ExpectQueryAsync("q", "type:exe has:config");
        await Expect(annotations.Filter).ToHaveValueAsync("type:exe has:config");
        await Expect(sheet.OpenButton).ToHaveTextAsync("2");

        await sheet.OpenAsync();
        await sheet.ClearAllButton.ClickAsync();
        await sheet.DoneAsync();
        await App.ExpectQueryAsync("q", null);
        await Expect(annotations.Filter).ToHaveValueAsync(string.Empty);
    }

    // The rows (the phone's items) of the filtered catalog are exactly the keys, each once.
    private static async Task ExpectRowsAsync(AnnotationsPage annotations, params string[] keys)
    {
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(keys.Length);
        foreach (var key in keys)
        {
            await Expect(annotations.Item(key)).ToHaveCountAsync(1);
        }
    }

    // A term that filters nothing: every annotation is shown.
    [GeneratedRegex(@"^([\d,]+) of \1$")]
    private static partial Regex TotalOfTotal();

    // Without a term the count is the total alone.
    [GeneratedRegex(@"^[\d,]+$")]
    private static partial Regex TotalOnly();
}
