using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Sdk;

// Sdk.NSwag has no test project of its own; sform is its only consumer. The configurations client reads documents
// without parsing dates (ApiSdk.partial.cs), so a date-like string of a configuration stays the stored string, while the
// typed dates of the response still parse.
public class ConfigurationsApiClientDateTests
{
    private const string Renewal = "2026-10-20T12:00:00.1230000+02:00";
    private const string Day = "2026-10-20";

    private static readonly DateTimeOffset ModifiedAt = new(2026, 10, 19, 8, 30, 0, TimeSpan.Zero);

    private readonly ScriptedHttpHandler _http = new();

    [Fact]
    public async Task Composition_KeepsTheDateLikeStringsOfTheDocuments_AndParsesTheAuditStamp()
    {
        var json = $$"""
            {
              "AnnotationKey": "sbt.northwind",
              "Content": { "renewal": "{{Renewal}}", "day": "{{Day}}" },
              "Hash": "a1b2c3d4",
              "Sources": [
                {
                  "Id": "configuration:sbt.northwind",
                  "Kind": "Configuration",
                  "AnnotationType": "Subject",
                  "AnnotationKey": "sbt.northwind",
                  "Presence": "Present",
                  "Version": 2,
                  "Modified": { "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } },
                  "Content": { "renewal": "{{Renewal}}", "day": "{{Day}}" }
                }
              ],
              "Nodes": [ { "Index": 0, "AnnotationKey": "sbt.northwind", "Depth": 0 } ],
              "Steps": [ { "Order": 1, "SourceId": "configuration:sbt.northwind", "Node": 0 } ],
              "Origins": [ { "Path": "/renewal", "Step": 1 }, { "Path": "/day", "Step": 1 } ]
            }
            """;
        _http.Respond(HttpStatusCode.OK, json);

        var composition = await CreateClient().GetConfigurationCompositionAsync("org1", "proj1", "default", "sbt.northwind", null);

        _http.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/api/v1/org1/proj1/default/configuration/sbt.northwind/composition");
        ShouldKeepTheStrings(composition.Content.ShouldBeOfType<JObject>());
        var source = composition.Sources.ShouldHaveSingleItem();
        ShouldKeepTheStrings(source.Content.ShouldBeOfType<JObject>());
        source.Modified.At.ShouldBe(ModifiedAt);
        composition.Nodes.Single().MergedInto.ShouldBeNull();

        // without the setting of the client, the same response would carry dates
        JsonConvert.DeserializeObject<JObject>(json)!["Content"]!["renewal"]!.Type.ShouldBe(JTokenType.Date);
    }

    [Fact]
    public async Task Effective_KeepsTheDateLikeStringsOfTheDocument()
    {
        _http.Respond(HttpStatusCode.OK, $$"""
            {
              "AnnotationKey": "sbt.northwind",
              "Version": 2,
              "Content": { "renewal": "{{Renewal}}", "day": "{{Day}}" },
              "Modified": { "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } }
            }
            """);

        var configuration = await CreateClient().GetConfigurationAsync("org1", "proj1", "default", "sbt.northwind", null, null);

        ShouldKeepTheStrings(configuration.Content.ShouldBeOfType<JObject>());
        configuration.Modified.At.ShouldBe(ModifiedAt);
    }

    private static void ShouldKeepTheStrings(JObject content)
    {
        content["renewal"]!.Type.ShouldBe(JTokenType.String);
        content["day"]!.Type.ShouldBe(JTokenType.String);
        content.ToString(Formatting.None).ShouldBe($$"""{"renewal":"{{Renewal}}","day":"{{Day}}"}""");
    }

    private ConfigurationsApiClient CreateClient()
    {
        return new ConfigurationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = "http://localhost:7071/api" });
    }
}
