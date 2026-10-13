using System;
using System.Linq;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands.KeyValues;

public class KeyValueFormatsTests
{
    [Fact]
    public void Env_ParsesCommentsExportQuotesAndEscapes()
    {
        var text = string.Join('\n',
            "# database",
            "DB__HOST=sql.internal   # inline comment",
            "export DB__PORT=5432",
            "DB__PASSWORD=\"p#ss \\\"word\\\"\\nline2\"",
            "API__KEY='keep $this #raw'",
            string.Empty,
            "EMPTY=");

        KeyValueFormats.ParseEnv(text).ShouldBe(
        [
            ("db.host", "sql.internal"),
            ("db.port", "5432"),
            ("db.password", "p#ss \"word\"\nline2"),
            ("api.key", "keep $this #raw"),
            ("empty", string.Empty),
        ]);
    }

    [Fact]
    public void Env_RoundTrips_AndQuotesWhereNeeded()
    {
        var entries = new[]
        {
            ("db.host", "sql.internal"),
            ("db.password", "p#ss \"word\"\nline2"),
            ("note", "two words"),
            ("empty", string.Empty),
        };

        var text = KeyValueFormats.SerializeEnv(entries);

        text.Split('\n').ShouldBe(["DB__HOST=sql.internal", "DB__PASSWORD=\"p#ss \\\"word\\\"\\nline2\"", "NOTE=\"two words\"", "EMPTY=\"\""]);
        KeyValueFormats.ParseEnv(text).ShouldBe(entries);
        Should.Throw<FormatException>(() => KeyValueFormats.ParseEnv("just text")).Message.ShouldContain("Line 1");
        Should.Throw<FormatException>(() => KeyValueFormats.ParseEnv("A=\"open"));
    }

    [Fact]
    public void Flat_FlattensNestedObjects_AndRoundTripsFlatOnes()
    {
        var flat = KeyValueFormats.ParseFlat("""{ "db": { "host": "x", "port": 5432, "ssl": true, "tags": ["a", "b"] }, "name": "billing", "empty": null }""");

        flat.ShouldBe([("db.host", "x"), ("db.port", "5432"), ("db.ssl", "true"), ("db.tags", "[\"a\",\"b\"]"), ("name", "billing"), ("empty", string.Empty)]);
        KeyValueFormats.ParseFlat(KeyValueFormats.SerializeFlat(flat)).ShouldBe(flat);
        Should.Throw<FormatException>(() => KeyValueFormats.ParseFlat("\"text\""));
    }

    [Theory]
    [InlineData(".env", null, KeyValueFileFormat.Env)]
    [InlineData("prod.env", null, KeyValueFileFormat.Env)]
    [InlineData("x.json", "{ \"Format\": \"2s.key-values.v1\", \"Items\": [] }", KeyValueFileFormat.Json)]
    [InlineData("x.json", "{ \"db.host\": \"x\" }", KeyValueFileFormat.Flat)]
    [InlineData("x.txt", "A=1", KeyValueFileFormat.Env)]
    [InlineData(null, "{ broken", KeyValueFileFormat.Json)]
    public void Detect_LooksAtTheNameThenTheContent(string? path, string? content, KeyValueFileFormat expected)
    {
        KeyValueFormats.Detect(path, content).ShouldBe(expected);
    }

    [Theory]
    [InlineData("json", KeyValueFileFormat.Json)]
    [InlineData("FLAT", KeyValueFileFormat.Flat)]
    [InlineData("dotenv", KeyValueFileFormat.Env)]
    public void TryParseFormat_AcceptsTheNames(string text, KeyValueFileFormat expected)
    {
        KeyValueFormats.TryParseFormat(text, out var format).ShouldBeTrue();
        format.ShouldBe(expected);
        KeyValueFormats.TryParseFormat("yaml", out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_GivesFlatAndEnvEntriesTheLevelOfTheirKey_AndSerializeSkipsSecretsWithoutAValue()
    {
        var document = KeyValueFormats.Parse("DB__HOST=x\nDB__PASSWORD=s", KeyValueFileFormat.Env, key => key.EndsWith("password", StringComparison.Ordinal) ? KeyValueDocumentItemProtection.Standard : KeyValueDocumentItemProtection.Public);

        document.Format.ShouldBe("2s.key-values.v1");
        document.Items.Select(item => (item.Key, item.Protection, item.Value)).ShouldBe([("db.host", KeyValueDocumentItemProtection.Public, "x"), ("db.password", KeyValueDocumentItemProtection.Standard, "s")]);

        var exported = new KeyValueDocument
        {
            Format = "2s.key-values.v1",
            Items = [new KeyValueDocumentItem { Key = "a", Protection = KeyValueDocumentItemProtection.Public, Value = "1" }, new KeyValueDocumentItem { Key = "s", Protection = KeyValueDocumentItemProtection.Standard }],
        };

        KeyValueFormats.Serialize(exported, KeyValueFileFormat.Env, out var skipped).ShouldBe("A=1");
        skipped.ShouldBe(["s"]);
        KeyValueFormats.Serialize(exported, KeyValueFileFormat.Json, out skipped).ShouldContain("\"s\"");
        skipped.ShouldBeEmpty();

        var parsedBack = KeyValueFormats.Parse(KeyValueFormats.Serialize(exported, KeyValueFileFormat.Json, out _), KeyValueFileFormat.Json, _ => KeyValueDocumentItemProtection.Public);
        parsedBack.Items.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("db.password", "db.*", true)]
    [InlineData("DB.PASSWORD", "*password", true)]
    [InlineData("db.pass", "db.pas?", true)]
    [InlineData("api.key", "db.*", false)]
    [InlineData("db.host.name", "db.?", false)]
    public void Glob_MatchesWholeKeysCaseInsensitively(string key, string glob, bool expected)
    {
        KeyGlob.IsMatch(key, glob).ShouldBe(expected);
        KeyGlob.IsMatch(key, (string[]?)null).ShouldBeFalse();
    }

    [Fact]
    public void Levels_ParseAndConvert()
    {
        KeyValueLevels.TryParse("hsm", out var level).ShouldBeTrue();
        level.ShouldBe(KeyValueProtection.Hsm);
        KeyValueLevels.TryParse("7", out _).ShouldBeFalse();
        KeyValueLevels.TryParse("vault", out _).ShouldBeFalse();
        KeyValueLevels.To<KeyValueWriteProtection>(KeyValueProtection.Byok).ShouldBe(KeyValueWriteProtection.Byok);
        KeyValueLevels.IsSecret(KeyValueSummaryProtection.Public).ShouldBeFalse();
        KeyValueLevels.IsSecret(KeyValueSummaryProtection.Standard).ShouldBeTrue();
        KeyValueLevels.IsClientEncrypted(KeyValueProtection.Hyok).ShouldBeTrue();
    }
}
