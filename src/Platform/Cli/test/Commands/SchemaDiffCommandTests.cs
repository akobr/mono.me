using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Schemas;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// schema type|annotation|descendant diff --format unified (SCH-TYP-03, SCH-ANN-03, SCH-DSC-03). The API answers
// ?format=unified as text/plain, which the typed client of the SDK cannot read: it threw an ApiException of the 200
// ("The request failed with 200 OK.") and sform ended with 1. sform now asks for the JSON diff and writes the unified
// text of the API itself.
public class SchemaDiffCommandTests
{
    private const string Organization = AnnotationCommandFixture.Organization;
    private const string Project = AnnotationCommandFixture.Project;
    private const string View = AnnotationCommandFixture.View;

    private const string ExpectedUnified =
        "@@ -1,4 +1,5 @@\n" +
        " {\n" +
        "   \"properties\": {\n" +
        "-    \"first\": { \"type\": \"string\" }\n" +
        "+    \"first\": { \"type\": \"string\" },\n" +
        "+    \"third\": { \"type\": \"string\" }\n" +
        "   }\n";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<ISchemasApiClient> _api = new(MockBehavior.Strict);

    public SchemaDiffCommandTests()
    {
        _fixture.Console.UseInnerConsole(redirectedInput: false);
    }

    [Theory]
    [InlineData("unified")]
    [InlineData("Unified")]
    [InlineData(" UNIFIED ")]
    public async Task TypeDiff_Unified_AsksForTheJsonDiff_AndWritesTheUnifiedText(string format)
    {
        _api.Setup(api => api.GetSchemaVersionDiffCustomAsync(Organization, Project, View, "unt", 3, 1, null)).ReturnsAsync(Diff());

        var exitCode = await new SchemaTypeDiffCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationType = "unt",
            ToVersion = "3",
            FromVersion = "1",
            Format = format,
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.OutputText.ShouldBe(ExpectedUnified);
        _fixture.Console.ErrorText.ShouldBeEmpty();
        _fixture.Console.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnnotationDiff_Unified_OfTheLatestVersion()
    {
        _api.Setup(api => api.GetAnnotationSchemaVersionsAsync(Organization, Project, View, "rst.billing"))
            .ReturnsAsync([new ConfigurationVersion { Version = 2 }, new ConfigurationVersion { Version = 3 }]);
        _api.Setup(api => api.GetAnnotationSchemaVersionDiffAsync(Organization, Project, View, "rst.billing", 3, null)).ReturnsAsync(Diff());

        var exitCode = await new SchemaAnnotationDiffCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationKey = "rst.billing",
            Format = "unified",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.OutputText.ShouldBe(ExpectedUnified);
    }

    [Fact]
    public async Task DescendantDiff_Unified_OfOneVersion()
    {
        _api.Setup(api => api.GetDescendantTypeSchemaVersionDiffAsync(Organization, Project, View, "rst.billing", "unt", 2, null)).ReturnsAsync(Diff());

        var exitCode = await new SchemaDescendantDiffCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationKey = "rst.billing",
            AnnotationType = "unt",
            ToVersion = "2",
            Format = "unified",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.OutputText.ShouldBe(ExpectedUnified);
    }

    // A unified diff without changes is empty, as the API's; the note goes to standard error.
    [Fact]
    public async Task Diff_Unified_WithoutChanges_WritesNothingToTheStandardOutput()
    {
        _api.Setup(api => api.GetSchemaVersionDiffAsync(Organization, Project, View, "unt", 2, null))
            .ReturnsAsync(new DiffResult { Stats = new DiffStats { Unchanged = 3 }, Hunks = [] });

        var exitCode = await new SchemaTypeDiffCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationType = "unt",
            ToVersion = "2",
            Format = "unified",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.OutputText.ShouldBeEmpty();
        _fixture.Console.ErrorLines.ShouldBe(["No changes detected."]);
    }

    // Any other --format still goes to the API, which answers JSON for it.
    [Fact]
    public async Task Diff_Json_IsPassedToTheApi()
    {
        _api.Setup(api => api.GetSchemaVersionDiffAsync(Organization, Project, View, "unt", 2, "json"))
            .ReturnsAsync(new DiffResult { Stats = new DiffStats(), Hunks = [] });

        var exitCode = await new SchemaTypeDiffCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationType = "unt",
            ToVersion = "2",
            Format = "json",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.Lines.ShouldBe(["No changes detected."]);
        _fixture.Console.OutputText.ShouldBeEmpty();
    }

    [Fact]
    public void ToUnifiedDiff_IsTheTextOfTheApi()
    {
        DiffResultConsoleExtensions.ToUnifiedDiff(Diff()).ShouldBe(ExpectedUnified);
        DiffResultConsoleExtensions.ToUnifiedDiff(new DiffResult()).ShouldBeEmpty();
    }

    // Two hunks and lines without content, as the API sends them (DiffFormatter.ToUnifiedDiff).
    [Fact]
    public void ToUnifiedDiff_OfSeveralHunks()
    {
        var diff = new DiffResult
        {
            Hunks =
            [
                new DiffHunk { OldStart = 1, OldCount = 1, NewStart = 1, NewCount = 1, Lines = [Line(DiffLineType.Deletion, "a"), Line(DiffLineType.Addition, "b")] },
                new DiffHunk { OldStart = 10, OldCount = 2, NewStart = 10, NewCount = 1, Lines = [Line(DiffLineType.Unchanged, string.Empty), Line(DiffLineType.Deletion, "c")] },
            ],
        };

        DiffResultConsoleExtensions.ToUnifiedDiff(diff).ShouldBe("@@ -1,1 +1,1 @@\n-a\n+b\n@@ -10,2 +10,1 @@\n \n-c\n");
    }

    private static DiffResult Diff()
    {
        return new DiffResult
        {
            Stats = new DiffStats { Additions = 2, Deletions = 1, Unchanged = 3 },
            Hunks =
            [
                new DiffHunk
                {
                    OldStart = 1,
                    OldCount = 4,
                    NewStart = 1,
                    NewCount = 5,
                    Lines = new List<DiffLine>
                    {
                        Line(DiffLineType.Unchanged, "{"),
                        Line(DiffLineType.Unchanged, "  \"properties\": {"),
                        Line(DiffLineType.Deletion, "    \"first\": { \"type\": \"string\" }"),
                        Line(DiffLineType.Addition, "    \"first\": { \"type\": \"string\" },"),
                        Line(DiffLineType.Addition, "    \"third\": { \"type\": \"string\" }"),
                        Line(DiffLineType.Unchanged, "  }"),
                    },
                },
            ],
        };
    }

    private static DiffLine Line(DiffLineType type, string content)
    {
        return new DiffLine { Type = type, Content = content };
    }
}
