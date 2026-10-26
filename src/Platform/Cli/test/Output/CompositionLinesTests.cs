using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// The lines of the blame listing of 'config composition': the text of the indented JSON, the pointer of the value of
// each line and the step that set it.
public class CompositionLinesTests
{
    [Fact]
    public void Leaves_TakeTheStepOfTheirPointer_TheRootBracesNone()
    {
        var content = Parse("""{ "name": "billing", "retries": 3, "ratio": 1.0, "enabled": true, "owner": null }""");

        var lines = CompositionLines.Of(content, Origins(("/name", 1), ("/retries", 2), ("/ratio", 3), ("/enabled", 4), ("/owner", 5)));

        lines.ShouldBe(
        [
            ("{", string.Empty, null),
            ("  \"name\": \"billing\",", "/name", 1),
            ("  \"retries\": 3,", "/retries", 2),
            ("  \"ratio\": 1.0,", "/ratio", 3),
            ("  \"enabled\": true,", "/enabled", 4),
            ("  \"owner\": null", "/owner", 5),
            ("}", string.Empty, null),
        ]);
    }

    [Fact]
    public void NestedContainers_OpenAndCloseWithTheStepOfTheContainer()
    {
        var content = Parse("""{ "database": { "host": "sql", "pool": { "size": 8 } }, "mode": "batch" }""");

        var lines = CompositionLines.Of(content, Origins(("/database", 1), ("/database/host", 2), ("/database/pool", 1), ("/database/pool/size", 3), ("/mode", 4)));

        lines.ShouldBe(
        [
            ("{", string.Empty, null),
            ("  \"database\": {", "/database", 1),
            ("    \"host\": \"sql\",", "/database/host", 2),
            ("    \"pool\": {", "/database/pool", 1),
            ("      \"size\": 8", "/database/pool/size", 3),
            ("    }", "/database/pool", 1),
            ("  },", "/database", 1),
            ("  \"mode\": \"batch\"", "/mode", 4),
            ("}", string.Empty, null),
        ]);
    }

    [Fact]
    public void Arrays_PointToTheirItemsByIndex()
    {
        var content = Parse("""{ "hosts": [ "a", { "name": "b" }, [ 1 ] ] }""");

        var lines = CompositionLines.Of(content, Origins(("/hosts", 1), ("/hosts/0", 1), ("/hosts/1", 2), ("/hosts/1/name", 2), ("/hosts/2", 3), ("/hosts/2/0", 3)));

        lines.ShouldBe(
        [
            ("{", string.Empty, null),
            ("  \"hosts\": [", "/hosts", 1),
            ("    \"a\",", "/hosts/0", 1),
            ("    {", "/hosts/1", 2),
            ("      \"name\": \"b\"", "/hosts/1/name", 2),
            ("    },", "/hosts/1", 2),
            ("    [", "/hosts/2", 3),
            ("      1", "/hosts/2/0", 3),
            ("    ]", "/hosts/2", 3),
            ("  ]", "/hosts", 1),
            ("}", string.Empty, null),
        ]);
    }

    [Fact]
    public void EmptyContainers_TakeOneLine()
    {
        var content = Parse("""{ "labels": {}, "hosts": [] }""");

        var lines = CompositionLines.Of(content, Origins(("/labels", 1), ("/hosts", 2)));

        lines.ShouldBe(
        [
            ("{", string.Empty, null),
            ("  \"labels\": {},", "/labels", 1),
            ("  \"hosts\": []", "/hosts", 2),
            ("}", string.Empty, null),
        ]);
    }

    [Fact]
    public void AnEmptyDocument_IsOneLineWithoutAStep()
    {
        CompositionLines.Of(new JObject(), []).ShouldBe([("{}", string.Empty, null)]);
    }

    [Fact]
    public void TheRoot_HasNoStep_EvenWhenAnOriginNamesIt()
    {
        var lines = CompositionLines.Of(Parse("""{ "a": 1 }"""), Origins((string.Empty, 7), ("/a", 1)));

        lines.First().Step.ShouldBeNull();
        lines.Last().Step.ShouldBeNull();
    }

    [Fact]
    public void Names_AreEscapedInTheTextAndInThePointer()
    {
        var content = Parse("""{ "a/b": 1, "m~n": 2, "~1": 3, "say \"hi\"": 4, "": 5 }""");

        var lines = CompositionLines.Of(content, Origins(("/a~1b", 1), ("/m~0n", 2), ("/~01", 3), ("/say \"hi\"", 4), ("/", 5)));

        lines.Skip(1).SkipLast(1).ShouldBe(
        [
            ("  \"a/b\": 1,", "/a~1b", 1),
            ("  \"m~n\": 2,", "/m~0n", 2),
            ("  \"~1\": 3,", "/~01", 3),
            ("  \"say \\\"hi\\\"\": 4,", "/say \"hi\"", 4),
            ("  \"\": 5", "/", 5),
        ]);
    }

    [Fact]
    public void AValueWithoutAnOrigin_HasNoStep()
    {
        var lines = CompositionLines.Of(Parse("""{ "a": 1, "b": 2 }"""), Origins(("/a", 1)));

        lines.Single(line => line.Pointer == "/b").Step.ShouldBeNull();
    }

    // The document comes through the real client, as 'config composition' reads it: the client keeps date-like strings
    // as strings (ApiSdk.partial.cs), and the lines show them as stored.
    [Fact]
    public async Task ADateLikeString_IsKeptAsItIs()
    {
        const string content = """{ "renewal": "2026-10-20T12:00:00.1230000+02:00", "day": "2026-10-20" }""";
        var http = new ScriptedHttpHandler().Respond(HttpStatusCode.OK, $$"""
            {
              "AnnotationKey": "sbt.northwind",
              "Content": {{content}},
              "Origins": [ { "Path": "/renewal", "Step": 1 }, { "Path": "/day", "Step": 1 } ]
            }
            """);
        var client = new ConfigurationsApiClient(new HttpClient(http), new SdkConfiguration { BaseUrl = "http://localhost:7071/api" });

        var composition = await client.GetConfigurationCompositionAsync("org1", "proj1", "default", "sbt.northwind", null);
        var lines = CompositionLines.Of(composition.Content.ShouldBeOfType<JObject>(), composition.Origins);

        lines[1].ShouldBe(("  \"renewal\": \"2026-10-20T12:00:00.1230000+02:00\",", "/renewal", 1));
        lines[2].ShouldBe(("  \"day\": \"2026-10-20\"", "/day", 1));

        // parsed with Newtonsoft's defaults, the same document would show the date in another form
        CompositionLines.Of(JObject.Parse(content), composition.Origins)[1].Text.ShouldNotBe(lines[1].Text);
    }

    [Fact]
    public void TheText_IsTheIndentedJsonOfNewtonsoft()
    {
        var content = Parse("""
            {
              "a": { "b": [ 1, 2.5, "x", null, {}, [], { "c": [ true ] } ], "d": {} },
              "e/f": "g\nh",
              "unicode": "č",
              "big": 12345678901234567890
            }
            """);

        var text = string.Join("\n", CompositionLines.Of(content, []).Select(line => line.Text));

        text.ShouldBe(content.ToString(Formatting.Indented).ReplaceLineEndings("\n"));
    }

    // As the SDK reads a configuration: date-like strings stay strings.
    private static JObject Parse(string json)
    {
        using var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    private static List<ConfigurationCompositionOrigin> Origins(params (string Path, int Step)[] origins)
    {
        return origins.Select(origin => new ConfigurationCompositionOrigin { Path = origin.Path, Step = origin.Step }).ToList();
    }
}
