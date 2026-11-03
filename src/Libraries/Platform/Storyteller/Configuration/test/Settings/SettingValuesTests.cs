using _42.Platform.Storyteller.Sdk.Configuring.Settings;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Settings;

public class SettingValuesTests
{
    [Theory]
    [InlineData("500ms", 500)]
    [InlineData("30s", 30_000)]
    [InlineData("5m", 300_000)]
    [InlineData("1h", 3_600_000)]
    [InlineData("1.5s", 1_500)]
    [InlineData(" 30 S ", 30_000)]
    [InlineData("30", 30_000)]
    [InlineData("0", 0)]
    [InlineData("0.5", 500)]
    [InlineData("00:00:30", 30_000)]
    [InlineData("01:00:00", 3_600_000)]
    [InlineData("1.00:00:00", 86_400_000)]
    public void Durations(string text, long milliseconds)
    {
        SettingValues.TryParseDuration(text, out var duration).ShouldBeTrue();
        duration.ShouldBe(TimeSpan.FromMilliseconds(milliseconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-5s")]
    [InlineData("-00:00:30")]
    [InlineData("30x")]
    [InlineData("5min")]
    [InlineData("s")]
    [InlineData("1,5s")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("99999999999999999h")]
    public void InvalidDurations(string text)
    {
        SettingValues.TryParseDuration(text, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" True ", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    public void Booleans(string text, bool expected)
    {
        SettingValues.TryParseBoolean(text, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("2")]
    [InlineData("")]
    public void InvalidBooleans(string text)
    {
        SettingValues.TryParseBoolean(text, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("resolved", StorytellerReadMode.Resolved)]
    [InlineData("RESOLVED", StorytellerReadMode.Resolved)]
    [InlineData("effective", StorytellerReadMode.Effective)]
    [InlineData(" Effective ", StorytellerReadMode.Effective)]
    public void Modes(string text, StorytellerReadMode expected)
    {
        SettingValues.TryParseMode(text, out var mode).ShouldBeTrue();
        mode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("raw")]
    public void InvalidModes(string text)
    {
        SettingValues.TryParseMode(text, out _).ShouldBeFalse();
    }
}
