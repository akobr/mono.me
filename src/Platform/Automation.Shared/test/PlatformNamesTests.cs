namespace _42.Platform.Automation.Shared.UnitTests;

// The Platform's names of organizations, projects and views: 2 to 63 lower-case letters, digits and hyphens.
public sealed class PlatformNamesTests
{
    [Theory]
    [InlineData("ab")]
    [InlineData("acme")]
    [InlineData("ui-tests")]
    [InlineData("1st-project")]
    [InlineData("a-")]
    public void IsValid_TakesAPlatformName(string name)
    {
        PlatformNames.IsValid(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("-ab")]
    [InlineData("Acme")]
    [InlineData("ui_tests")]
    [InlineData("acme\n")]
    [InlineData("acme/billing")]
    public void IsValid_RefusesAnythingElse(string? name)
    {
        PlatformNames.IsValid(name).ShouldBeFalse();
    }

    [Fact]
    public void IsValid_TakesAtMost63Characters()
    {
        PlatformNames.IsValid(new string('a', 63)).ShouldBeTrue();
        PlatformNames.IsValid(new string('a', 64)).ShouldBeFalse();
    }
}
