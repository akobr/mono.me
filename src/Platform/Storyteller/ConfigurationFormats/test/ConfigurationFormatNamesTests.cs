namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

public class ConfigurationFormatNamesTests
{
    [Fact]
    public void TheNames_AreTheCanonicalNamesOfEveryFormat()
    {
        ConfigurationFormatNames.Names.ShouldBe(["flat", "properties", "env", "yaml"]);
        Enum.GetValues<ConfigurationFormat>().Select(format => format.GetName()).ShouldBe(ConfigurationFormatNames.Names);
    }

    [Theory]
    [InlineData("flat", ConfigurationFormat.Flat)]
    [InlineData("FLAT", ConfigurationFormat.Flat)]
    [InlineData("Properties", ConfigurationFormat.Properties)]
    [InlineData("eNv", ConfigurationFormat.Env)]
    [InlineData("YAML", ConfigurationFormat.Yaml)]
    public void TryParse_TakesTheNames_InAnyCasing(string value, ConfigurationFormat expected)
    {
        ConfigurationFormatNames.TryParse(value, out var format).ShouldBeTrue();

        format.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" flat")]
    [InlineData("flat ")]
    [InlineData("json")]
    [InlineData("yml")]
    [InlineData("1")]
    [InlineData("flat,yaml")]
    [InlineData("flat,flat")]
    [InlineData("propert\u0131es")]
    public void TryParse_RefusesAnythingElse(string? value)
    {
        ConfigurationFormatNames.TryParse(value, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(ConfigurationFormat.Flat, "application/json; charset=utf-8")]
    [InlineData(ConfigurationFormat.Properties, "text/plain; charset=utf-8")]
    [InlineData(ConfigurationFormat.Env, "text/plain; charset=utf-8")]
    [InlineData(ConfigurationFormat.Yaml, "application/yaml; charset=utf-8")]
    public void TheMediaType_IsTheOneOfTheBody(ConfigurationFormat format, string mediaType)
    {
        format.GetMediaType().ShouldBe(mediaType);
        ConfigurationFormatter.Format(null, format).MediaType.ShouldBe(mediaType);
    }

    [Fact]
    public void AnUndefinedFormat_HasNoName()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((ConfigurationFormat)0).GetName());
        Should.Throw<ArgumentOutOfRangeException>(() => ((ConfigurationFormat)5).GetMediaType());
    }
}
