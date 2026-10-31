namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests;

public sealed class TestinyCaseIdsTests
{
    [Theory]
    [InlineData("1234", 1234)]
    [InlineData(" 1234 ", 1234)]
    [InlineData("TC-1234", 1234)]
    [InlineData("tc_77", 77)]
    [InlineData("TC1234", 1234)]
    public void TryParse_TakesTheDigits(string id, long expected)
    {
        TestinyCaseIds.TryParse(id, out var caseId).ShouldBeTrue();
        caseId.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TC-")]
    [InlineData("0")]
    [InlineData("12a")]
    [InlineData("TC-12-3")]
    [InlineData("P2-55")]
    [InlineData("1234567890123456789")]
    [InlineData(null)]
    public void TryParse_RefusesWhatIsNoId(string? id)
    {
        TestinyCaseIds.TryParse(id, out _).ShouldBeFalse();
    }
}
