using System.Collections.Generic;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

public class TransferConsoleExtensionsTests
{
    [Theory]
    [InlineData(ViewImportItemKind.Annotation, "rst.payments", null, "Annotation", "rst.payments")]
    [InlineData(ViewImportItemKind.Configuration, "rst.payments", null, "Configuration", "rst.payments")]
    [InlineData(ViewImportItemKind.Template, null, "exe", "Template", "exe")]
    [InlineData(ViewImportItemKind.TypeSchema, null, "exe", "Type schema", "exe")]
    [InlineData(ViewImportItemKind.AnnotationSchema, "rst.payments", null, "Annotation schema", "rst.payments")]
    [InlineData(ViewImportItemKind.DescendantTypeSchema, "sbt.northwind", "exe", "Descendant-type schema", "exe below sbt.northwind")]
    public void Items_NameTheirKindAndDocument(ViewImportItemKind kind, string? key, string? type, string expectedKind, string expectedDocument)
    {
        var item = new ViewImportItem { Kind = kind, AnnotationKey = key, AnnotationType = type, Path = "some/file.json" };

        TransferConsoleExtensions.FormatKind(kind).ShouldBe(expectedKind);
        TransferConsoleExtensions.FormatDocument(item).ShouldBe(expectedDocument);
    }

    [Fact]
    public void Items_WithoutKeyOrType_ShowTheirPath()
    {
        var item = new ViewImportItem { Kind = ViewImportItemKind.Configuration, Path = "configurations/odd.conf.json" };

        TransferConsoleExtensions.FormatDocument(item).ShouldBe("configurations/odd.conf.json");
    }

    [Fact]
    public void ItemRows_HaveTheWarningsUnderTheirRow_MarkedWithAnExclamation()
    {
        var items = new List<ViewImportItem>
        {
            new()
            {
                Kind = ViewImportItemKind.Annotation, AnnotationKey = "exe.n.p.prod", Outcome = ViewImportItemOutcome.Created,
                Warnings =
                [
                    new ViewImportWarning { Code = "AncestorCreated", Message = "The missing ancestors sbt.n are created with defaults." },
                    new ViewImportWarning { Code = "AnnotationRecreated", Message = "rst.p was deleted and is re-created." },
                ],
            },
            new()
            {
                Kind = ViewImportItemKind.TypeSchema, AnnotationType = "exe", Outcome = ViewImportItemOutcome.Updated, Version = 3,
                Warnings = [new ViewImportWarning { Code = "SchemaViolation", Message = "2 configurations violate the schema." }],
            },
            new()
            {
                Kind = ViewImportItemKind.Configuration, AnnotationKey = "rst.q", Outcome = ViewImportItemOutcome.Skipped,
                ErrorCode = "DocumentExists", Message = "The view already has a different document.",
            },
        };

        TransferConsoleExtensions.GetItemRows(items).ShouldBe(
        [
            ["Annotation", "exe.n.p.prod", "Created", string.Empty, string.Empty],
            [string.Empty, string.Empty, "!", "AncestorCreated", "The missing ancestors sbt.n are created with defaults."],
            [string.Empty, string.Empty, "!", "AnnotationRecreated", "rst.p was deleted and is re-created."],
            ["Type schema", "exe", "Updated", string.Empty, string.Empty],
            [string.Empty, string.Empty, "!", "SchemaViolation", "2 configurations violate the schema."],
            ["Configuration", "rst.q", "Skipped", "DocumentExists", "The view already has a different document."],
        ]);
    }

    [Fact]
    public void SummaryRows_ArePerKindInTheServersOrder_ThenTheTotal()
    {
        var import = new ViewImport
        {
            Summary = new ViewImportSummary { Created = 3, Updated = 2, Unchanged = 7, Skipped = 1, Failed = 1, Warnings = 2, ExcludedFiles = 4, IgnoredFiles = 5 },
            Summaries =
            [
                new ViewImportSummary { Content = ViewImportSummaryContent.Annotations, Created = 2, Unchanged = 5, Warnings = 1 },
                new ViewImportSummary { Content = ViewImportSummaryContent.Schemas, Updated = 1, Failed = 1 },
                new ViewImportSummary { Content = ViewImportSummaryContent.Templates, Unchanged = 2 },
                new ViewImportSummary { Content = ViewImportSummaryContent.Configurations, Created = 1, Updated = 1, Skipped = 1, Warnings = 1 },
            ],
        };

        TransferConsoleExtensions.GetSummaryRows(import).ShouldBe(
        [
            ["Annotations", "2", "0", "5", "0", "0", "1"],
            ["Schemas", "0", "1", "0", "0", "1", "0"],
            ["Templates", "0", "0", "2", "0", "0", "0"],
            ["Configurations", "1", "1", "0", "1", "0", "1"],
            ["Total", "3", "2", "7", "1", "1", "2"],
        ]);
    }

    [Fact]
    public void SummaryHeaders_SayWhatADryRunWouldDo()
    {
        TransferConsoleExtensions.GetSummaryHeaders(false).ShouldBe(["Kind", "Created", "Updated", "Unchanged", "Skipped", "Failed", "Warnings"]);
        TransferConsoleExtensions.GetSummaryHeaders(true).ShouldBe(["Kind", "Would create", "Would update", "Unchanged", "Skipped", "Failed", "Warnings"]);
    }

    [Fact]
    public void WriteImport_OfADryRunWithoutItems_WritesOnlyTheSummary()
    {
        var console = new RecordingConsole();
        var import = new ViewImport
        {
            IsDryRun = true,
            Summary = new ViewImportSummary { Unchanged = 4, ExcludedFiles = 1, IgnoredFiles = 2 },
            Summaries = [new ViewImportSummary { Content = ViewImportSummaryContent.Annotations, Unchanged = 4 }],
            Items = [],
        };

        console.Object.WriteImport(import);

        var table = console.Tables.ShouldHaveSingleItem();
        table.Headers[1].ShouldBe("Would create");
        table.Rows.ShouldBe([["Annotations", "0", "0", "4", "0", "0", "0"], ["Total", "0", "0", "4", "0", "0", "0"]]);
        console.Lines.ShouldBe(["1 file excluded, 2 other files ignored.", "Nothing was written (dry run)."]);
    }

    [Fact]
    public void WriteImport_SaysWhenTheItemsAreTruncated()
    {
        var console = new RecordingConsole();
        var import = new ViewImport
        {
            Summary = new ViewImportSummary { Failed = 2 },
            Items = [new ViewImportItem { Kind = ViewImportItemKind.Annotation, AnnotationKey = "rst.a", Outcome = ViewImportItemOutcome.Failed }],
            AreItemsTruncated = true,
        };

        console.Object.WriteImport(import);

        console.Tables.Count.ShouldBe(2);
        console.Tables[1].Rows.ShouldBe([["Total", "0", "0", "0", "0", "2", "0"]]);
        console.Lines[0].ShouldBe("Only the first 1 files are listed; the counts include all.");
    }

    [Theory]
    [InlineData(403, ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(400, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(413, ExitCodes.ERROR_WRONG_INPUT)]
    public void WriteRefusal_PrintsTheServersMessage_AndOnlyA403IsUnauthorized(int status, int expected)
    {
        var console = new RecordingConsole();

        var exitCode = console.Object.WriteRefusal(AnnotationCommandFixture.Error(status, "Refused", $"Refused with {status}."));

        exitCode.ShouldBe(expected);
        console.Lines.ShouldBe([$"Refused with {status}."]);
    }

    [Fact]
    public void ExportSummary_CountsTheIncludedKinds_WithVersionsOnlyForVersionedKinds()
    {
        var all = new ViewExport { AnnotationCount = 1, SchemaCount = 2, TemplateCount = 0, ConfigurationCount = 12, VersionCount = 30, Size = 3 * 1024 * 1024 / 2 };
        var annotations = new ViewExport { AnnotationCount = 5, VersionCount = 0, Size = 900 };
        var templates = new ViewExport { TemplateCount = 1, VersionCount = 1, Size = 1536 };
        var none = new ViewExport { Size = null };

        TransferConsoleExtensions.FormatExportSummary(all).ShouldBe("1 annotation, 2 schemas, 0 templates and 12 configurations (30 versions, 1.5 MB)");
        TransferConsoleExtensions.FormatExportSummary(annotations).ShouldBe("5 annotations (900 B)");
        TransferConsoleExtensions.FormatExportSummary(templates).ShouldBe("1 template (1 version, 1.5 KB)");
        TransferConsoleExtensions.FormatExportSummary(none).ShouldBe("no documents (unknown size)");
    }
}
