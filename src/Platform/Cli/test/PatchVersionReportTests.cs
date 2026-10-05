using _42.Platform.Cli.Json;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

public class PatchVersionReportTests
{
    [Fact]
    public void IncreasedVersion_UsesTheArrow()
    {
        var report = PatchVersionReport.Create("exe.northwind.invoicing.prod", 4, 5);

        report.Changed.ShouldBeTrue();
        report.Message.ShouldBe("Configuration for 'exe.northwind.invoicing.prod' patched: version 4 → 5.");
    }

    [Fact]
    public void UnchangedVersion_SaysStillThatVersion()
    {
        var report = PatchVersionReport.Create("exe.northwind.invoicing.prod", 4, 4);

        report.Changed.ShouldBeFalse();
        report.Message.ShouldBe("Patch did not change the stored content of 'exe.northwind.invoicing.prod'. Still version 4.");
    }

    [Fact]
    public void VersionMovedByMoreThanOne_StillUsesTheArrow()
    {
        var report = PatchVersionReport.Create("exe.northwind.invoicing.prod", 4, 6);

        report.Changed.ShouldBeTrue();
        report.Message.ShouldBe("Configuration for 'exe.northwind.invoicing.prod' patched: version 4 → 6.");
    }
}
