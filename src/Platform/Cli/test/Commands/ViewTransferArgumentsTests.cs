using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class ViewTransferArgumentsTests
{
    [Fact]
    public void Include_Missing_IsNull_ForAllFourKinds()
    {
        ViewTransferArguments.TryParseInclude(null, out var missing, out _).ShouldBeTrue();
        ViewTransferArguments.TryParseInclude([], out var empty, out _).ShouldBeTrue();

        missing.ShouldBeNull();
        empty.ShouldBeNull();
    }

    [Fact]
    public void Include_TakesTheKindsInAnyCase_CommaSeparatedOrRepeated_DistinctInImportOrder()
    {
        ViewTransferArguments.TryParseInclude(["Configurations, templates", "ANNOTATIONS,,schemas", "templates"], out var include, out var error)
            .ShouldBeTrue();

        error.ShouldBeNull();
        include.ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations]);
    }

    [Theory]
    [InlineData("annotation", "Unknown kind 'annotation' in --include; use annotations, schemas, templates or configurations, comma-separated.")]
    [InlineData("2", "Unknown kind '2' in --include; use annotations, schemas, templates or configurations, comma-separated.")]
    [InlineData("schemas;templates", "Unknown kind 'schemas;templates' in --include; use annotations, schemas, templates or configurations, comma-separated.")]
    [InlineData(",", "The option --include names no kind; use annotations, schemas, templates or configurations, comma-separated, or leave it out for all four.")]
    public void Include_RejectsUnknownKinds(string value, string message)
    {
        ViewTransferArguments.TryParseInclude([value], out var include, out var error).ShouldBeFalse();

        include.ShouldBeNull();
        error.ShouldBe(message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("overwrite", ViewImportExisting.Overwrite)]
    [InlineData("Skip", ViewImportExisting.Skip)]
    [InlineData(" SKIP ", ViewImportExisting.Skip)]
    public void Existing_TakesOverwriteOrSkipInAnyCase(string? value, ViewImportExisting? expected)
    {
        ViewTransferArguments.TryParseExisting(value, out var existing, out var error).ShouldBeTrue();

        existing.ShouldBe(expected);
        error.ShouldBeNull();
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("1")]
    [InlineData("")]
    public void Existing_RejectsOtherValues(string value)
    {
        ViewTransferArguments.TryParseExisting(value, out var existing, out var error).ShouldBeFalse();

        existing.ShouldBeNull();
        error.ShouldBe($"Unknown value '{value}' of --existing; use 'overwrite' or 'skip'.");
    }

    [Theory]
    [InlineData("folder", ViewExportCreateFormat.Folder)]
    [InlineData("Git", ViewExportCreateFormat.Git)]
    public void Format_TakesFolderOrGitInAnyCase(string value, ViewExportCreateFormat expected)
    {
        ViewTransferArguments.TryParseFormat(value, out var format, out _).ShouldBeTrue();

        format.ShouldBe(expected);
    }

    [Fact]
    public void Kinds_AreListedInWords_AllFourByDefault()
    {
        ViewTransferArguments.FormatKinds(null).ShouldBe("annotations, schemas, templates and configurations");
        ViewTransferArguments.FormatKinds([ViewArchiveContent.Templates]).ShouldBe("templates");
        ViewTransferArguments.FormatKinds([ViewArchiveContent.Schemas, ViewArchiveContent.Configurations]).ShouldBe("schemas and configurations");
    }
}
