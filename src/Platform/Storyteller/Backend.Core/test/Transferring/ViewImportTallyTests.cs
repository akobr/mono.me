using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewImportTallyTests
{
    private static readonly ViewImportWarning Warning = new() { Code = ErrorCodes.AncestorCreated, Message = "The ancestors were created." };

    [Fact]
    public void Add_CountsEveryOutcomeInTotalAndPerKind()
    {
        var tally = new ViewImportTally(null, total: 7, ignoredFiles: 2, excludedFiles: 1);

        tally.Add(File("rst.a.json", ViewDocumentKind.Annotation, key: "rst.a"), new ViewImportResult(ViewImportOutcome.Created, Warnings: [Warning]));
        tally.Add(File("rst.b.json", ViewDocumentKind.Annotation, key: "rst.b"), new ViewImportResult(ViewImportOutcome.Unchanged, Warnings: [Warning]));
        tally.Add(File("exe.schema.json", ViewDocumentKind.TypeSchema, type: "exe"), new ViewImportResult(ViewImportOutcome.Updated, 2));
        tally.Add(File("rst.a.schema.json", ViewDocumentKind.AnnotationSchema, key: "rst.a"), new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: ErrorCodes.DocumentExists, Message: "kept"));
        tally.Add(File("exe.template.json", ViewDocumentKind.Template, type: "exe"), new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.PatchInvalid, Message: "patch"));
        tally.Add(File("rst.a.conf.json", ViewDocumentKind.Configuration, key: "rst.a"), new ViewImportResult(ViewImportOutcome.Created, 1, Warnings: [Warning]));

        tally.Total.ShouldBe(7);
        tally.Processed.ShouldBe(6);

        // an unchanged document is counted, but neither as a warning nor as an item
        tally.Summary.ShouldBe(new ViewImportSummary { Created = 2, Updated = 1, Unchanged = 1, Skipped = 1, Failed = 1, Warnings = 2, ExcludedFiles = 1, IgnoredFiles = 2 });
        tally.Summaries.ShouldBe(
        [
            new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 1, Unchanged = 1, Warnings = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Schemas, Updated = 1, Skipped = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Templates, Failed = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Configurations, Created = 1, Warnings = 1 },
        ]);
        tally.Items.Select(item => (item.Path, item.Kind, item.AnnotationType, item.AnnotationKey, item.Outcome, item.Version, item.ErrorCode, item.Message)).ShouldBe(
        [
            ("rst.a.json", ViewDocumentKind.Annotation, null, "rst.a", ViewImportOutcome.Created, null, null, null),
            ("exe.schema.json", ViewDocumentKind.TypeSchema, "exe", null, ViewImportOutcome.Updated, 2UL, null, null),
            ("rst.a.schema.json", ViewDocumentKind.AnnotationSchema, null, "rst.a", ViewImportOutcome.Skipped, null, ErrorCodes.DocumentExists, "kept"),
            ("exe.template.json", ViewDocumentKind.Template, "exe", null, ViewImportOutcome.Failed, null, ErrorCodes.PatchInvalid, "patch"),
            ("rst.a.conf.json", ViewDocumentKind.Configuration, null, "rst.a", ViewImportOutcome.Created, 1UL, null, null),
        ]);
        tally.Items[0].Warnings.ShouldBe([Warning]);
        tally.AreItemsTruncated.ShouldBeFalse();
    }

    [Fact]
    public void Summaries_FollowTheIncludedKindsInImportOrder()
    {
        var tally = new ViewImportTally([ViewArchiveContent.Configurations, ViewArchiveContent.Annotations, ViewArchiveContent.Configurations], total: 2);

        tally.Add(File("rst.a.conf.json", ViewDocumentKind.Configuration, key: "rst.a"), new ViewImportResult(ViewImportOutcome.Created, 1));

        // a kind left out counts in the total only
        tally.Add(File("exe.template.json", ViewDocumentKind.Template, type: "exe"), new ViewImportResult(ViewImportOutcome.Created, 1));

        tally.Summaries.Select(summary => (summary.Content, summary.Created)).ShouldBe(
        [
            (ViewArchiveContent.Annotations, 0),
            (ViewArchiveContent.Configurations, 1),
        ]);
        tally.Summary.ShouldBe(new ViewImportSummary { Created = 2 });
    }

    [Fact]
    public void Items_StopAtTheLimitAndAreACopy()
    {
        var tally = new ViewImportTally(null, total: 4, maxItems: 1);
        var failed = new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidDocument, Message: "broken");
        var before = tally.Items;

        tally.Add(File("rst.a.conf.json", ViewDocumentKind.Configuration, key: "rst.a"), failed);

        // an unchanged document is no item, so it does not truncate them
        tally.Add(File("rst.b.conf.json", ViewDocumentKind.Configuration, key: "rst.b"), new ViewImportResult(ViewImportOutcome.Unchanged, 1));
        tally.AreItemsTruncated.ShouldBeFalse();

        tally.Add(File("rst.c.conf.json", ViewDocumentKind.Configuration, key: "rst.c"), failed);

        tally.Items.Select(item => item.Path).ShouldBe(["rst.a.conf.json"]);
        tally.AreItemsTruncated.ShouldBeTrue();
        tally.Summary.Failed.ShouldBe(2);
        before.ShouldBeEmpty();
    }

    private static ViewArchiveFile File(string path, ViewDocumentKind kind, string? type = null, string? key = null)
    {
        return new ViewArchiveFile
        {
            Path = path,
            Kind = kind,
            AnnotationType = type,
            AnnotationKey = key is null ? null : AnnotationKey.Parse(key),
        };
    }
}
