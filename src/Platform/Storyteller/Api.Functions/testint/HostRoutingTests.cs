using System.Net;
using System.Text;
using System.Text.Json;

namespace _42.Platform.Storyteller.Api.Functions.IntegrationTests;

// The routing of a running Functions host (HostTestSettings). The host runs the first function, in the order of the
// function names, whose route matches; the route constraints of the API (Definitions.Routes.Constraints) keep a
// parameter off its literal siblings, so each request below reaches its own function (424-128). The requests change
// nothing. RouteDispatchTests of the unit tests models the same dispatch without a host.
//
// Start the platform locally (dotnet run --project src/Platform/Aspire.Host/src), set the variables, and run
// dotnet test --project src/Platform/Storyteller/Api.Functions/testint
public class HostRoutingTests(ITestOutputHelper output)
{
    // SetAnnotation took it before, with the key "simple": 400 "No annotation object in body."
    [Fact]
    public async Task BulkCreate_OfNoLines_ReachesSetAnnotationsSimple()
    {
        if (Settings() is not { } settings)
        {
            return;
        }

        using var client = settings.CreateClient();
        using var content = new StringContent("[]", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"{settings.ViewPath}/annotations/simple", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.ValueKind.ShouldBe(JsonValueKind.Array, body);
        document.RootElement.GetArrayLength().ShouldBe(0, body);
    }

    // GetAnnotation took it before, with the key "deleted": 400 "Invalid annotation key: deleted" (424-131).
    [Fact]
    public async Task DeletedList_ReachesGetDeletedAnnotations()
    {
        if (Settings() is not { } settings)
        {
            return;
        }

        using var client = settings.CreateClient();
        using var response = await client.GetAsync($"{settings.ViewPath}/annotations/deleted");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("Annotations").ValueKind.ShouldBe(JsonValueKind.Array, body);
    }

    // GetKeyValues took it before, with the view "usage": the key/value list (Items) instead of the report (Days).
    // The report is for Administrators; a Contributor gets 403 from the same function.
    [Fact]
    public async Task UsageReport_ReachesGetKeyValueUsage()
    {
        if (Settings() is not { } settings)
        {
            return;
        }

        using var client = settings.CreateClient();
        using var response = await client.GetAsync($"{settings.ProjectPath}/usage/key-values");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Forbidden], body);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(body);
            document.RootElement.TryGetProperty("Days", out _).ShouldBeTrue(body);
            document.RootElement.TryGetProperty("Items", out _).ShouldBeFalse(body);
        }
    }

    // A key needs a dot to reach a {key} route; GetAnnotation answered 400 "Invalid annotation key: nope" before.
    [Fact]
    public async Task KeyWithoutADot_ReachesNoFunction()
    {
        if (Settings() is not { } settings)
        {
            return;
        }

        using var client = settings.CreateClient();
        using var response = await client.GetAsync($"{settings.ViewPath}/annotations/nope");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    private HostTestSettings? Settings()
    {
        var settings = HostTestSettings.TryRead();

        if (settings is null)
        {
            output.WriteLine($"Skipped: set {HostTestSettings.Variables} to run against a Functions host.");
        }

        return settings;
    }
}
