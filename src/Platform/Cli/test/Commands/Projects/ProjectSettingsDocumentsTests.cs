using System.Linq;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands.Projects;

// The input rules of sform project settings set (--type, --icon) and the tables the output takes from the catalog.
public class ProjectSettingsDocumentsTests
{
    [Theory]
    [InlineData("sbt", "sbt")]
    [InlineData("SBT", "sbt")]
    [InlineData("subject", "sbt")]
    [InlineData("Subject", "sbt")]
    [InlineData(" responsibility ", "rst")]
    [InlineData("unit of execution", "uxe")]
    [InlineData("Unit-of-execution", "uxe")]
    [InlineData("UnitOfExecution", "uxe")]
    [InlineData("unit", "unt")]
    public void TypeCode_IsACodeOrABuiltInName(string value, string code)
    {
        ProjectSettingsDocuments.TryGetTypeCode(value, out var found).ShouldBeTrue();
        found.ShouldBe(code);
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("subjects")]
    [InlineData("solution")]
    [InlineData("")]
    [InlineData(null)]
    public void TypeCode_IsNeverAnotherName(string? value)
    {
        ProjectSettingsDocuments.TryGetTypeCode(value, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("building-2", "i-lucide-building-2")]
    [InlineData("i-lucide-building-2", "i-lucide-building-2")]
    [InlineData("I-Lucide-Building-2", "I-Lucide-Building-2")]
    [InlineData(" sun ", "i-lucide-sun")]
    [InlineData("", "")]
    public void IconName_GetsThePrefixOfTheCatalog(string value, string icon)
    {
        ProjectSettingsDocuments.ToIconName(value).ShouldBe(icon);
    }

    [Fact]
    public void EveryIconOfTheCatalog_IsALucideIcon()
    {
        ProjectSettingsCatalog.Default.Icons.ShouldAllBe(icon => icon.StartsWith("i-lucide-"));
    }

    [Theory]
    [InlineData("Job", "Jobs")]
    [InlineData("Policy", "Policies")]
    [InlineData("Day", "Days")]
    [InlineData("Batch", "Batches")]
    [InlineData("Box", "Boxes")]
    [InlineData("Class", "Classes")]
    [InlineData("Wish", "Wishes")]
    [InlineData("Y", "Ys")]
    public void Plural_IsDerivedAsTheAdminUiDerivesIt(string name, string plural)
    {
        ProjectSettingsDocuments.Pluralize(name).ShouldBe(plural);
    }

    [Fact]
    public void EveryColorOfTheCatalog_HasAConsoleColor()
    {
        TypeColors.Names.OrderBy(name => name).ShouldBe(ProjectSettingsCatalog.Default.Colors.OrderBy(name => name));

        foreach (var color in ProjectSettingsCatalog.Default.Colors)
        {
            TypeColors.TryGetConsoleColor(color, out var consoleColor).ShouldBeTrue(color);
            consoleColor.ShouldNotBe(System.ConsoleColor.DarkGray, color);
        }
    }

    [Fact]
    public void TheETag_IsTheQuotedVersion()
    {
        ProjectSettingsDocuments.ETagOf(7).ShouldBe("\"7\"");
    }
}
