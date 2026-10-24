using System.Globalization;
using System.Net;

using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using ModelContextProtocol.Client;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The management tools through the MCP endpoint and the SDK clients, against the recording API.
public sealed class ManagementToolsHostTests : IDisposable
{
    private const string Key = "exe.northwind.billing.prod";
    private const string Base = "https://api.test/api/v1/house/main/default";

    private const string AnnotationJson =
        """{"AnnotationType":"Execution","ProjectName":"main","ViewName":"default","AnnotationKey":"exe.northwind.billing.prod","Name":"prod"}""";

    private static readonly string[] ManagementTools =
    [
        "list_annotations", "get_annotation", "find_annotations", "list_descendants", "set_annotation", "create_annotations",
        "delete_annotation", "list_deleted_annotations", "restore_annotations", "purge_annotation", "get_annotation_history",
        "list_configurations", "get_configuration", "set_configuration", "replace_configuration", "revert_configuration",
        "patch_configuration", "delete_configuration", "list_configuration_versions", "diff_configuration",
        "list_schemas", "get_schema", "set_schema", "delete_schema", "get_combined_schema", "list_schema_versions", "diff_schema",
        "list_templates", "get_template", "set_template", "replace_template", "patch_template", "delete_template",
        "list_template_versions", "diff_template",
    ];

    private readonly McpTestHost _host = new();

    [Fact]
    public async Task Catalog_ListsTheManagementTools()
    {
        await using var client = await _host.ConnectAsync();

        var names = (await client.ListToolsAsync()).Select(tool => tool.Name).ToList();

        ManagementTools.ShouldAllBe(name => names.Contains(name));
    }

    [Fact]
    public async Task SetConfiguration_ChecksTheAnnotation_ThenSendsTheDocument()
    {
        _host.Api.Respond = request => request.Method == HttpMethod.Get
            ? RecordingApiHandler.Json(AnnotationJson)
            : RecordingApiHandler.Json("""{"AnnotationKey":"exe.northwind.billing.prod","Version":2,"Content":{"retries":3}}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("set_configuration", new { organization = "house", project = "main", key = Key, content = new { retries = 3, hosts = new[] { "a" } } });

        result.IsError.ShouldNotBe(true, result.Text());
        var requests = _host.Api.Requests.ToList();
        requests.Count.ShouldBe(2);
        requests[0].Uri.ToString().ShouldBe($"{Base}/annotations/{Key}?includeInactive=true&includeDeleted=true");
        requests[1].Method.ShouldBe(HttpMethod.Post);
        requests[1].Uri.ToString().ShouldBe($"{Base}/configuration/{Key}");
        JToken.DeepEquals(JToken.Parse(requests[1].Body!), JObject.Parse("""{"retries":3,"hosts":["a"]}""")).ShouldBeTrue(requests[1].Body);
    }

    [Fact]
    public async Task ReplaceConfiguration_SendsAJsonPatchWithTests()
    {
        _host.Api.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith($"/annotations/{Key}", StringComparison.Ordinal) => RecordingApiHandler.Json(AnnotationJson),
            var path when path.EndsWith("/versions/4", StringComparison.Ordinal) => RecordingApiHandler.Json("""{"Version":4,"Content":{"retries":3,"owner":"a"}}"""),
            _ when request.Method == HttpMethod.Get => RecordingApiHandler.Json("""{"Version":4,"Content":{"retries":3,"owner":"a","inherited":true}}"""),
            _ => RecordingApiHandler.Json("""{"AnnotationKey":"exe.northwind.billing.prod","Version":5,"Content":{"retries":4}}"""),
        };
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("replace_configuration", new { organization = "house", project = "main", key = Key, content = new { retries = 4 } });

        result.IsError.ShouldNotBe(true, result.Text());
        var patch = _host.Api.Requests.Last();
        patch.Method.ShouldBe(HttpMethod.Patch);
        JToken.DeepEquals(
                JToken.Parse(patch.Body!),
                JArray.Parse("""[{"op":"test","path":"/owner","value":"a"},{"op":"remove","path":"/owner"},{"op":"test","path":"/retries","value":3},{"op":"replace","path":"/retries","value":4}]"""))
            .ShouldBeTrue(patch.Body);
        result.Text().ShouldContain("\"Changed\":true");
    }

    [Fact]
    public async Task ReplaceConfiguration_FailedTest_IsAToolErrorWithTheCode()
    {
        _host.Api.Respond = request => request.Method == HttpMethod.Patch
            ? RecordingApiHandler.Json("""{"Message":"JSON Patch operation 0 failed.","ErrorCode":"PatchTestFailed"}""", HttpStatusCode.PreconditionFailed)
            : request.RequestUri!.AbsolutePath.Contains("/annotations/", StringComparison.Ordinal)
                ? RecordingApiHandler.Json(AnnotationJson)
                : RecordingApiHandler.Json("""{"Version":1,"Content":{"retries":3}}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("replace_configuration", new { organization = "house", project = "main", key = Key, content = new { retries = 4 } });

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("412 PatchTestFailed");
    }

    [Fact]
    public async Task SetAnnotation_TitleOnly_SendsTheStoredValuesAsTheyAreStored()
    {
        // date-like strings, nested ones too, with offsets and milliseconds a parsed DateTime would not keep
        const string values = """{"d1":"2026-11-01T10:00:00.000+02:00","d2":"2026-11-01T10:00:00.000Z","o":{"x":"2026-11-01T10:00:00+05:00","list":["2026-01-01T00:00:00.5Z"]}}""";
        _host.Api.Respond = _ => RecordingApiHandler.Json(
            $$"""{"AnnotationType":"Subject","ProjectName":"main","ViewName":"default","AnnotationKey":"sbt.northwind","Name":"northwind","Title":"Old","ValidFrom":"2026-11-01T00:00:00+01:00","Values":{{values}}}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("set_annotation", new { organization = "house", project = "main", key = "sbt.northwind", title = "Northwind" });

        result.IsError.ShouldNotBe(true, result.Text());
        var write = _host.Api.Requests.Last();
        write.Method.ShouldBe(HttpMethod.Post);
        var body = (JObject)AsText(write.Body!);
        body.Value<string>("Title").ShouldBe("Northwind");
        body["Values"]!.ToString(Formatting.None).ShouldBe(values);
        DateTimeOffset.Parse(body.Value<string>("ValidFrom")!, CultureInfo.InvariantCulture).ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task ReplaceTemplate_ChangedDate_IsTestedWithTheStoredText()
    {
        _host.Api.Respond = request => request.Method == HttpMethod.Get
            ? RecordingApiHandler.Json("""{"AnnotationType":"exe","Version":2,"Content":{"since":"2026-10-24T10:00:00.000Z","n":1}}""")
            : RecordingApiHandler.Json("""{"AnnotationType":"exe","Version":3,"Content":{"since":"2026-10-25T10:00:00.000Z","n":1}}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("replace_template", new { organization = "house", project = "main", annotationType = "exe", content = new { since = "2026-10-25T10:00:00.000Z", n = 1 } });

        result.IsError.ShouldNotBe(true, result.Text());
        var patch = _host.Api.Requests.Last();
        patch.Method.ShouldBe(HttpMethod.Patch);
        AsText(patch.Body!).ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"/since","value":"2026-10-24T10:00:00.000Z"},{"op":"replace","path":"/since","value":"2026-10-25T10:00:00.000Z"}]""");
    }

    [Fact]
    public async Task GetSchema_KeepsDateLikeStringsAsStored()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(
            """{"AnnotationType":"exe","Version":1,"Content":{"type":"object","properties":{"at":{"type":"string","default":"2026-10-24T10:00:00.000+02:00"}}}}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_schema", new { organization = "house", project = "main", level = "type", annotationType = "exe" });

        result.IsError.ShouldNotBe(true, result.Text());
        result.Text().ShouldContain("\"default\":\"2026-10-24T10:00:00.000+02:00\"");
    }

    [Fact]
    public async Task CreateAnnotations_DeletedAncestor_IsAToolError_AndCreatesNothing()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(
            """[{"AnnotationType":"Subject","ProjectName":"main","ViewName":"default","AnnotationKey":"sbt.northwind","Name":"northwind","Deletion":{"DeletionId":"d1","RootKey":"sbt.northwind","DeletedAt":"2026-10-20T08:00:00+00:00","IsRoot":true}}]""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("create_annotations", new { organization = "house", project = "main", lines = new[] { "northwind.billing.prod" } });

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("restore_annotations");
        var lookup = _host.Api.Requests.ShouldHaveSingleItem();
        lookup.Uri.AbsolutePath.ShouldEndWith("/annotations/deleted/lookup");
        JObject.Parse(lookup.Body!)["Keys"]!.Values<string>().ShouldContain(Key);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    // A request body as the API reads it: date-like strings stay text.
    private static JToken AsText(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }
}
