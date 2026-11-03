using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

public class ConfigurationFormatterTests
{
    public static TheoryData<ConfigurationFormat> Formats()
    {
        return new TheoryData<ConfigurationFormat>(Enum.GetValues<ConfigurationFormat>());
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryFormat_WritesLfLineEndsAndKeepsTheDocumentOrder(ConfigurationFormat format)
    {
        var text = ConfigurationFormatter.Format(
            JObject.Parse("""{ "z": "v1", "a": { "y": "v2", "b": "v3" }, "m": [ "v4", "v5\r\nv6" ] }"""),
            format).Text;

        text.ShouldNotContain('\r');
        text.ShouldNotStartWith("\uFEFF");
        text.ShouldEndWith("\n");
        text.IndexOf("v1", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("v2", StringComparison.Ordinal));
        text.IndexOf("v2", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("v3", StringComparison.Ordinal));
        text.IndexOf("v3", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("v4", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void ANullContent_FormatsAsAnEmptyDocument(ConfigurationFormat format)
    {
        var empty = ConfigurationFormatter.Format(new JObject(), format).Text;

        ConfigurationFormatter.Format(null, format).Text.ShouldBe(empty);
        ConfigurationFormatter.Format(JValue.CreateNull(), format).Text.ShouldBe(empty);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void AContentThatIsNotAnObject_IsInvalid_InEveryFormat(ConfigurationFormat format)
    {
        var exception = Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.Format(JArray.Parse("""[ "s3cret" ]"""), format));

        exception.Rule.ShouldBe(DocumentRules.NotAnObject);
        exception.Path.ShouldBe("$");
    }

    [Theory]
    [InlineData(ConfigurationFormat.Flat, """{ "Db": { "Password": "s3cret" }, "db": { "password": "0th3r" } }""", "case-collision", "$.db.password")]
    [InlineData(ConfigurationFormat.Yaml, """{ "db": { "a:b": "s3cret" } }""", "colon-in-name", "$.db['a:b']")]
    [InlineData(ConfigurationFormat.Properties, """{ "db": { "a[0]": "s3cret" } }""", "bracket-in-name", "$.db['a[0]']")]
    [InlineData(ConfigurationFormat.Properties, """{ "list": [ "s3cret", null, "0th3r" ] }""", "array-gap", "$.list[1]")]
    [InlineData(ConfigurationFormat.Env, """{ "db": { "pass word": "s3cret" } }""", "invalid-env-name", "$.db['pass word']")]
    public void AnError_NamesTheRuleAndThePath_ButNoValue(ConfigurationFormat format, string json, string rule, string path)
    {
        var exception = Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.Format(JObject.Parse(json), format));

        exception.Rule.ShouldBe(rule);
        exception.Path.ShouldBe(path);
        exception.Message.ShouldContain(path);
        exception.Message.ShouldNotContain("s3cret");
        exception.Message.ShouldNotContain("0th3r");
    }

    [Fact]
    public void TheBaseRules_ApplyToEveryFormat_BeforeItsOwnRules()
    {
        // A colon in a name is invalid in env too, where it is not a delimiter, and before the space is.
        var exception = Should.Throw<InvalidDocumentException>(
            () => ConfigurationFormatter.Format(JObject.Parse("""{ "a b": { "c:d": 1 } }"""), ConfigurationFormat.Env));

        exception.Rule.ShouldBe(DocumentRules.ColonInName);
    }

    [Fact]
    public void Canonicalize_FormatsTheTokensOfTheServer_AsAClientReadsThem()
    {
        // As Cosmos DB and the binding converter parse it: a date token and a decimal, and an unpaired surrogate.
        var server = new JObject
        {
            ["renewal"] = new DateTimeOffset(2026, 10, 20, 12, 0, 0, 123, TimeSpan.FromHours(2)),
            ["ratio"] = 1.50m,
            ["lone"] = "\uD800x",
        };

        // As a client reads the envelope: the content as the API writes it, in UTF-8, read with the SDK's settings.
        var client = SdkJson.Parse(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(server.ToString(Formatting.None))));

        foreach (var format in Enum.GetValues<ConfigurationFormat>())
        {
            ConfigurationFormatter.Format(server, format).Text.ShouldBe(ConfigurationFormatter.Format(client, format).Text);
        }

        var keys = DocumentFlattener.Flatten(ConfigurationFormatter.Canonicalize(server));
        keys["renewal"].ShouldBe("2026-10-20T12:00:00.123+02:00");
        keys["ratio"].ShouldBe("1.5");
        keys["lone"].ShouldBe("\uFFFDx");

        // Without it, the server's tokens flatten differently.
        DocumentFlattener.Flatten(server)["renewal"].ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        DocumentFlattener.Flatten(server)["ratio"].ShouldBe("1.50");
    }

    [Fact]
    public void Canonicalize_ReplacesUnpairedSurrogates_InNamesAndValues_AndKeepsPairs()
    {
        var content = new JObject { ["a\uD800"] = "\uDC00b", ["emoji"] = "\uD83D\uDE00" };

        var canonical = ConfigurationFormatter.Canonicalize(content).ShouldBeOfType<JObject>();

        canonical.Properties().Select(property => property.Name).ShouldBe(["a\uFFFD", "emoji"]);
        canonical.Value<string>("a\uFFFD").ShouldBe("\uFFFDb");
        canonical.Value<string>("emoji").ShouldBe("\uD83D\uDE00");
        content.Value<string>("a\uD800").ShouldBe("\uDC00b");
    }

    [Fact]
    public void Canonicalize_KeepsANullContent()
    {
        ConfigurationFormatter.Canonicalize(null).ShouldBeNull();
        ConfigurationFormatter.Canonicalize(JValue.CreateNull())!.Type.ShouldBe(JTokenType.Null);
    }

    [Fact]
    public void ToEnvironmentVariables_ReturnsTheNamesAndTheRawValues()
    {
        var content = JObject.Parse("""{ "db": { "host": "a b", "port": 5432, "ssl": true } }""");

        ConfigurationFormatter.ToEnvironmentVariables(content).ShouldBe(
        [
            new EnvironmentVariable("DB__HOST", "a b", DocumentValueKind.String, "$.db.host"),
            new EnvironmentVariable("DB__PORT", "5432", DocumentValueKind.Number, "$.db.port"),
            new EnvironmentVariable("DB__SSL", "true", DocumentValueKind.Boolean, "$.db.ssl"),
        ]);
        ConfigurationFormatter.ToEnvironmentVariables(content, "app_").Select(variable => variable.Name)
            .ShouldBe(["APP_DB__HOST", "APP_DB__PORT", "APP_DB__SSL"]);
    }

    [Fact]
    public void ThePrefix_IsUpperCasedAndPrepended_WithoutASeparator()
    {
        var text = ConfigurationFormatter.Format(JObject.Parse("""{ "db": { "host": "a b" } }"""), ConfigurationFormat.Env, "app_").Text;

        text.ShouldBe("APP_DB__HOST='a b'\n");
    }

    [Fact]
    public void ThePrefix_PlaysNoPartInTheRules()
    {
        var content = JObject.Parse("""{ "0a": 1 }""");

        Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.Format(content, ConfigurationFormat.Env, "APP_"))
            .Rule.ShouldBe(DocumentRules.InvalidEnvName);
        Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.ToEnvironmentVariables(content, "APP_"))
            .Rule.ShouldBe(DocumentRules.InvalidEnvName);
    }

    [Theory]
    [InlineData(ConfigurationFormat.Flat)]
    [InlineData(ConfigurationFormat.Properties)]
    [InlineData(ConfigurationFormat.Yaml)]
    public void ThePrefix_IsRefused_ForTheOtherFormats(ConfigurationFormat format)
    {
        Should.Throw<ArgumentException>(() => ConfigurationFormatter.Format(new JObject(), format, "APP_"))
            .ParamName.ShouldBe("environmentPrefix");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("_A")]
    [InlineData("A-B")]
    [InlineData("A B")]
    [InlineData("\u010CA")]
    public void AnInvalidPrefix_IsRefused(string prefix)
    {
        Should.Throw<ArgumentException>(() => ConfigurationFormatter.Format(new JObject(), ConfigurationFormat.Env, prefix))
            .ParamName.ShouldBe("environmentPrefix");
        Should.Throw<ArgumentException>(() => ConfigurationFormatter.ToEnvironmentVariables(new JObject(), prefix))
            .ParamName.ShouldBe("environmentPrefix");
    }

    [Fact]
    public void AnUndefinedFormat_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ConfigurationFormatter.Format(new JObject(), (ConfigurationFormat)0));
    }

    [Fact]
    public void TheResults_LeaveTheirValuesOutOfToString()
    {
        var content = JObject.Parse("""{ "password": "s3cret" }""");

        ConfigurationFormatter.Format(content, ConfigurationFormat.Env).ToString().ShouldNotContain("s3cret");
        ConfigurationFormatter.ToEnvironmentVariables(content)[0].ToString().ShouldNotContain("s3cret");
    }
}
