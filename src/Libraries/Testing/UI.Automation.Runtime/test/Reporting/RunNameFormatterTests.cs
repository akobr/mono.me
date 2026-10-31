using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Reporting;

public sealed class RunNameFormatterTests
{
    private static readonly RunNameValues Values = new()
    {
        RunId = "20261031-142500-abcd",
        StartedAt = new DateTimeOffset(2026, 10, 31, 14, 25, 7, TimeSpan.FromHours(1)),
        Profile = "mobile",
        Provider = "Local",
        Engine = "chromium",
        Device = "Pixel 7",
        Branch = "feat/424-133",
        Sha = "268a67a2e4f1c0de",
        BuildNumber = "128",
        Machine = "ci-runner-3",
    };

    [Fact]
    public void Format_ResolvesTheDefaultPattern()
    {
        RunNameFormatter.Format(ReportingSettings.DefaultRunName, Values).ShouldBe("UI automation 2026-10-31 14:25 mobile");
    }

    [Fact]
    public void Format_CollapsesTheSpacesOfEmptyValues()
    {
        RunNameFormatter.Format(ReportingSettings.DefaultRunName, Values with { Profile = null }).ShouldBe("UI automation 2026-10-31 14:25");
        RunNameFormatter.Format("{Branch}  nightly {Profile} {Device}", Values with { Branch = null, Profile = null }).ShouldBe("nightly Pixel 7");
    }

    [Fact]
    public void Format_ResolvesEveryPlaceholder()
    {
        var name = RunNameFormatter.Format("{RunId}|{Date}|{Time}|{Profile}|{Provider}|{Engine}|{Device}|{Branch}|{Sha7}|{BuildNumber}|{Machine}", Values);

        name.ShouldBe("20261031-142500-abcd|2026-10-31|14:25|mobile|Local|chromium|Pixel 7|feat/424-133|268a67a|128|ci-runner-3");
    }

    [Fact]
    public void Format_TakesDateAndTimeFormats_CaseInsensitiveNames_AndEscapedBraces()
    {
        RunNameFormatter.Format("{date:dd.MM.yyyy} {TIME:HH-mm-ss} {{literal}} {sha7}", Values with { Sha = "abc" })
            .ShouldBe("31.10.2026 14-25-07 {literal} abc");
    }

    [Fact]
    public void Format_KeepsAnUnknownPlaceholder_AndTheSpacesOfADescription()
    {
        RunNameFormatter.Format("Run {Nope}", Values).ShouldBe("Run {Nope}");
        RunNameFormatter.Format("| A |  {Profile} |\n", Values, collapseSpaces: false).ShouldBe("| A |  mobile |\n");
    }

    [Fact]
    public void Validate_AcceptsEveryKnownPlaceholder()
    {
        RunNameFormatter.Validate(string.Join(" ", RunNameFormatter.Placeholders.Select(name => $"{{{name}}}")) + " {Date:yyyy} {{x}}").ShouldBeEmpty();
    }

    [Fact]
    public void Validate_NamesWhatIsWrong()
    {
        RunNameFormatter.Validate("{Nope}").ShouldHaveSingleItem().ShouldStartWith("the placeholder {Nope} is unknown; use one of {RunId}, {Date},");
        RunNameFormatter.Validate("{RunId:x}").ShouldBe(["the placeholder {RunId} takes no format."]);
        RunNameFormatter.Validate("Run {Date").ShouldBe(["the '{' at position 5 is not closed; write {{ for a literal brace."]);
    }
}
