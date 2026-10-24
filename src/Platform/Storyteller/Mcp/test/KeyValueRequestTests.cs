using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// What the key/value tools send to the API over HTTP, through the whole MCP host.
public sealed class KeyValueRequestTests : IDisposable
{
    private const string Pair = """{"Key":"db.password","Protection":"Standard","IsSecret":true,"Version":2}""";

    private readonly McpTestHost _host = new(new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "true" });

    [Fact]
    public async Task GetKeyValue_SendsRevealFalse()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(Pair);
        await using var client = await _host.ConnectAsync();

        var current = await client.CallAsync("get_key_value", new { organization = "house", project = "main", key = "db.password" });
        var older = await client.CallAsync("get_key_value", new { organization = "house", project = "main", key = "db.password", version = 1 });

        current.IsError.ShouldNotBe(true);
        older.IsError.ShouldNotBe(true);
        _host.Api.Requests.Select(request => request.Uri.PathAndQuery).ShouldBe(
        [
            "/api/v1/house/main/default/key-values/db.password?reveal=false",
            "/api/v1/house/main/default/key-values/db.password/versions/1?reveal=false",
        ]);
    }

    [Fact]
    public async Task RevealKeyValue_SendsRevealTrue()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(Pair);
        await using var client = await _host.ConnectAsync();

        await client.CallAsync("reveal_key_value", new { organization = "house", project = "main", key = "db.password" });

        _host.Api.Requests.ShouldHaveSingleItem().Uri.PathAndQuery.ShouldBe("/api/v1/house/main/default/key-values/db.password?reveal=true");
    }

    [Fact]
    public async Task DiffKeyValue_OfASecret_NeverReachesTheDiffRoute()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json("""[{"Version":1,"Protection":"Public","IsCurrent":false},{"Version":2,"Protection":"Standard","IsCurrent":true}]""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("diff_key_value", new { organization = "house", project = "main", key = "db.password", version = 2 });

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("is a secret (Standard)");
        _host.Api.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/api/v1/house/main/default/key-values/db.password/versions");
    }

    [Fact]
    public async Task SetKeyValue_Hyok_IsAToolErrorWithoutARequest()
    {
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("set_key_value", new { organization = "house", project = "main", key = "api.token", value = "{}", protection = "Hyok" });

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("client-encrypted value");
        _host.Api.Requests.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
