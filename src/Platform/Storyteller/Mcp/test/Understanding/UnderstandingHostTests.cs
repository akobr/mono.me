using System.Net;

using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

// The understanding tools through the MCP endpoint: arguments bound from JSON, the API reached through the SDK clients.
public sealed class UnderstandingHostTests : IDisposable
{
    private const string Secret = "S3CRET-VALUE-42";
    private const string Configuration = "/api/v1/acme/billing/default/configuration/exe.northwind.invoicing.prod";

    private readonly McpTestHost _host = new(new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "true" });

    public UnderstandingHostTests()
    {
        _host.Api.Respond = Respond;
    }

    [Fact]
    public async Task Placement_BindsTheSystems_AndReplaysTheComposition()
    {
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("suggest_configuration_placement", new
        {
            organization = "acme",
            project = "billing",
            systems = new[] { new { subject = "northwind", responsibility = "invoicing", context = "prod" } },
            pointers = new[] { "/currency" },
        });

        result.IsError.ShouldNotBe(true, result.Text());
        var suggestion = UnderstandingToolsTests.Structured(result);
        var usage = ((JArray)suggestion["Candidates"]!).Single(candidate => candidate["AnnotationKey"]!.Value<string>() == "usg.northwind.invoicing");
        usage["Effects"]![0]!["Effective"]!.Value<bool>().ShouldBeFalse();
        suggestion["Candidates"]![1]!["Evidence"]!["State"]!.Value<string>().ShouldBe("Missing");
        _host.Api.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get || request.Uri.AbsolutePath.EndsWith("/annotations/deleted/lookup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Placement_BindsAFragment()
    {
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("suggest_configuration_placement", new
        {
            organization = "acme",
            project = "billing",
            systems = new[] { new { subject = "northwind", responsibility = "invoicing", context = "prod" } },
            fragment = new { currency = "EUR", tags = new[] { "eu" } },
            typeDefault = true,
        });

        result.IsError.ShouldNotBe(true, result.Text());
        var suggestion = UnderstandingToolsTests.Structured(result);
        suggestion["Pointers"]!.Select(pointer => pointer.Value<string>()).ShouldBe(["/currency", "/tags"]);
        suggestion["Candidates"]![0]!["Kind"]!.Value<string>().ShouldBe("Template");
    }

    [Fact]
    public async Task Explain_AnswersThroughTheEndpoint()
    {
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("explain_configuration", new { organization = "acme", project = "billing", key = "exe.northwind.invoicing.prod", pointer = "/currency", includeConsumers = false });

        result.IsError.ShouldNotBe(true, result.Text());
        UnderstandingToolsTests.Structured(result)["Values"]![0]!["Step"]!.Value<int>().ShouldBe(4);
    }

    [Fact]
    public async Task BindingCheck_NeverReturnsTheResolvedValue()
    {
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("check_configuration_bindings", new { organization = "acme", project = "billing", key = "exe.northwind.invoicing.prod" });

        result.IsError.ShouldNotBe(true, result.Text());
        result.Text().ShouldNotContain(Secret);
        UnderstandingToolsTests.Structured(result)["Bindings"]![0]!["Resolved"]!.Value<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task BindingCheck_IsListedOnlyWithTheSecretTools()
    {
        using var plain = new McpTestHost();
        await using var withoutSecrets = await plain.ConnectAsync();
        await using var withSecrets = await _host.ConnectAsync();

        var plainNames = (await withoutSecrets.ListToolsAsync()).Select(tool => tool.Name).ToList();
        var secretNames = (await withSecrets.ListToolsAsync()).Select(tool => tool.Name).ToList();

        plainNames.ShouldContain("search_configurations");
        plainNames.ShouldContain("get_binding_catalog");
        plainNames.ShouldContain("explain_configuration");
        plainNames.ShouldContain("suggest_configuration_placement");
        plainNames.ShouldNotContain("check_configuration_bindings");
        secretNames.ShouldContain("check_configuration_bindings");
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        return path switch
        {
            Configuration + "/composition" => RecordingApiHandler.Json(WithBinding(Compositions.ExecutionJson())),
            Configuration + "/resolved" => RecordingApiHandler.Json($$$"""{"AnnotationKey":"exe.northwind.invoicing.prod","Version":0,"Content":{"currency":"NOK","password":"{{{Secret}}}"}}"""),
            Configuration => RecordingApiHandler.Json("""{"AnnotationKey":"exe.northwind.invoicing.prod","Version":0,"Hash":"c0ffee12","Content":{"currency":"NOK","password":"@secret(db.password)"}}"""),
            _ when path.EndsWith("/annotations/deleted/lookup", StringComparison.Ordinal) => RecordingApiHandler.Json("[]"),
            _ => RecordingApiHandler.Json("""{"Message":"Not found."}""", HttpStatusCode.NotFound),
        };
    }

    private static string WithBinding(string json)
    {
        var composition = JObject.Parse(json);
        composition["Content"]!["password"] = "@secret(db.password)";
        return composition.ToString();
    }
}
