using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public sealed class BoundaryTests : IDisposable
{
    private const string MetadataUrl = "https://mcp.test/.well-known/oauth-protected-resource/mcp";

    private readonly McpTestHost _host = new();

    public static TheoryData<string, string> InvalidTokens()
    {
        var otherKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "other" };

        return new TheoryData<string, string>
        {
            { "wrong audience", TestTokens.Create(audience: "api://someone-else") },
            { "issuer of another tenant", TestTokens.Create(issuer: $"https://login.microsoftonline.com/{TestTokens.OtherTenantId}/v2.0") },
            { "tid of another tenant", TestTokens.Create(overrides: new Dictionary<string, object?> { ["tid"] = TestTokens.OtherTenantId }) },
            { "no tid", TestTokens.Create(overrides: new Dictionary<string, object?> { ["tid"] = null }) },
            { "expired", TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-10)) },
            { "app-only", TestTokens.Create(overrides: new Dictionary<string, object?> { ["scp"] = null, ["roles"] = new[] { "Mcp.Access" } }) },
            { "idtyp app", TestTokens.Create(overrides: new Dictionary<string, object?> { ["idtyp"] = "app" }) },
            { "client app not allowed", TestTokens.Create(overrides: new Dictionary<string, object?> { ["azp"] = TestTokens.OtherClientAppId }) },
            { "unknown signing key", TestTokens.Create(signingKey: otherKey) },
        };
    }

    [Fact]
    public async Task NoToken_Answers401WithTheResourceMetadata()
    {
        using var client = _host.CreateClient();

        using var response = await client.SendAsync(McpRequest(token: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe($"Bearer resource_metadata=\"{MetadataUrl}\"");
    }

    [Fact]
    public async Task NoToken_ChallengeIgnoresTheHostHeader()
    {
        using var client = _host.CreateClient();
        using var request = McpRequest(token: null);
        request.Headers.Host = "evil.example";

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain($"resource_metadata=\"{MetadataUrl}\"");
    }

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task InvalidToken_Answers401(string because, string token)
    {
        using var client = _host.CreateClient();

        using var response = await client.SendAsync(McpRequest(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, because);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("error=\"invalid_token\"", Case.Sensitive, because);
        response.Headers.WwwAuthenticate.ToString().ShouldContain($"resource_metadata=\"{MetadataUrl}\"", Case.Sensitive, because);
    }

    [Fact]
    public async Task TokenWithoutTheMcpScope_Answers403InsufficientScope()
    {
        using var client = _host.CreateClient();
        var token = TestTokens.Create(overrides: new Dictionary<string, object?> { ["scp"] = "Other.Scope" });

        using var response = await client.SendAsync(McpRequest(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ToString().ShouldBe(
            $"Bearer error=\"insufficient_scope\", scope=\"{TestTokens.ResourceUrl}/{TestTokens.Scope}\", resource_metadata=\"{MetadataUrl}\"");
    }

    [Fact]
    public async Task V1TokenWithAppIdOfAnAllowedClient_IsAccepted()
    {
        await using var client = await _host.ConnectAsync(TestTokens.Create(
            issuer: $"https://sts.windows.net/{TestTokens.TenantId}/",
            audience: TestTokens.ResourceUrl,
            overrides: new Dictionary<string, object?> { ["azp"] = null, ["appid"] = TestTokens.ClientAppId, ["ver"] = "1.0" }));

        var tools = await client.ListToolsAsync();

        tools.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ValidToken_ListsTheTools()
    {
        await using var client = await _host.ConnectAsync();

        var tools = await client.ListToolsAsync();

        tools.Select(tool => tool.Name).ShouldContain("get_my_account");
        tools.Select(tool => tool.Name).ShouldContain("list_projects");
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    [InlineData("/.well-known/oauth-protected-resource")]
    public async Task ProtectedResourceMetadata_DescribesTheResource(string path)
    {
        using var client = _host.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(path));
        var root = document.RootElement;

        root.GetProperty("resource").GetString().ShouldBe(TestTokens.ResourceUrl);
        root.GetProperty("authorization_servers").EnumerateArray().Select(server => server.GetString()).ShouldBe(["https://mcp.test"]);
        root.GetProperty("scopes_supported").EnumerateArray().Select(scope => scope.GetString()).ShouldBe([$"{TestTokens.ResourceUrl}/{TestTokens.Scope}"]);
        root.GetProperty("bearer_methods_supported").EnumerateArray().Select(method => method.GetString()).ShouldBe(["header"]);
    }

    [Fact]
    public async Task AuthorizationServerMetadata_DescribesEntraWithPkce()
    {
        using var client = _host.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-authorization-server"));
        var root = document.RootElement;

        root.GetProperty("issuer").GetString().ShouldBe("https://mcp.test");
        root.GetProperty("authorization_endpoint").GetString().ShouldBe($"https://login.microsoftonline.com/{TestTokens.TenantId}/oauth2/v2.0/authorize");
        root.GetProperty("token_endpoint").GetString().ShouldBe($"https://login.microsoftonline.com/{TestTokens.TenantId}/oauth2/v2.0/token");
        root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(method => method.GetString()).ShouldBe(["S256"]);
        root.GetProperty("scopes_supported").EnumerateArray().Select(scope => scope.GetString()).ShouldContain($"{TestTokens.ResourceUrl}/{TestTokens.Scope}");
        root.TryGetProperty("registration_endpoint", out _).ShouldBeFalse();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static HttpRequestMessage McpRequest(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
