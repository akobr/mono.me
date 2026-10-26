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
            { "issuer of another provider", TestTokens.Create(issuer: TestTokens.AuthKitIssuer) },
            { "tid of another tenant", TestTokens.Create(overrides: new Dictionary<string, object?> { ["tid"] = TestTokens.OtherTenantId }) },
            { "no tid", TestTokens.Create(overrides: new Dictionary<string, object?> { ["tid"] = null }) },
            { "expired", TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-10)) },
            { "expired within the default skew", TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-2)) },
            { "expires within seconds", TestTokens.Create(expires: DateTime.UtcNow.AddSeconds(5)) },
            { "app-only", TestTokens.Create(overrides: new Dictionary<string, object?> { ["scp"] = null, ["roles"] = new[] { "Mcp.Access" } }) },
            { "idtyp app", TestTokens.Create(overrides: new Dictionary<string, object?> { ["idtyp"] = "app" }) },
            { "client app not allowed", TestTokens.Create(overrides: new Dictionary<string, object?> { ["azp"] = TestTokens.OtherClientAppId }) },
            { "unknown signing key", TestTokens.Create(signingKey: otherKey) },
        };
    }

    public static TheoryData<string, string, string> InvalidProviderTokens()
    {
        var otherKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "other" };

        return new TheoryData<string, string, string>
        {
            { "AuthKit", "wrong issuer", TestTokens.CreateAuthKit(issuer: "https://other.authkit.test") },
            { "AuthKit", "Entra issuer", TestTokens.CreateAuthKit(issuer: TestTokens.V2Issuer) },
            { "AuthKit", "wrong audience", TestTokens.CreateAuthKit(audience: "https://elsewhere.test/mcp") },
            { "AuthKit", "M2M token", TestTokens.CreateAuthKit(subject: TestTokens.AuthKitClientId) },
            { "AuthKit", "client not allowed", TestTokens.CreateAuthKit(overrides: new Dictionary<string, object?> { ["client_id"] = TestTokens.OtherAuthKitClientId }) },
            { "AuthKit", "expired", TestTokens.CreateAuthKit(expires: DateTime.UtcNow.AddMinutes(-10)) },
            { "AuthKit", "expired within the default skew", TestTokens.CreateAuthKit(expires: DateTime.UtcNow.AddMinutes(-2)) },
            { "AuthKit", "expires within seconds", TestTokens.CreateAuthKit(expires: DateTime.UtcNow.AddSeconds(5)) },
            { "AuthKit", "unknown signing key", TestTokens.CreateAuthKit(signingKey: otherKey) },
            { "Keycloak", "wrong issuer", TestTokens.CreateKeycloak(issuer: "https://keycloak.mcp.test/realms/other") },
            { "Keycloak", "wrong audience", TestTokens.CreateKeycloak(audience: "account") },
            { "Keycloak", "service account", TestTokens.CreateKeycloak(overrides: new Dictionary<string, object?> { ["client_id"] = TestTokens.KeycloakClientId, ["preferred_username"] = "service-account-mcp-client" }) },
            { "Keycloak", "ID token", TestTokens.CreateKeycloak(overrides: new Dictionary<string, object?> { ["typ"] = "ID" }) },
            { "Keycloak", "client not allowed", TestTokens.CreateKeycloak(overrides: new Dictionary<string, object?> { ["azp"] = TestTokens.OtherKeycloakClientId }) },
            { "Keycloak", "expired", TestTokens.CreateKeycloak(expires: DateTime.UtcNow.AddMinutes(-10)) },
            { "Keycloak", "expired within the default skew", TestTokens.CreateKeycloak(expires: DateTime.UtcNow.AddMinutes(-2)) },
            { "Keycloak", "expires within seconds", TestTokens.CreateKeycloak(expires: DateTime.UtcNow.AddSeconds(5)) },
            { "Keycloak", "unknown signing key", TestTokens.CreateKeycloak(signingKey: otherKey) },
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

        // Entra ID: the host itself, whose RFC 8414 document adds PKCE to Entra's endpoints.
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

    [Theory]
    [InlineData("AuthKit")]
    [InlineData("Keycloak")]
    public async Task IssuerProvider_ValidToken_ListsTheTools(string provider)
    {
        using var host = ProviderHost(provider);
        await using var client = await host.ConnectAsync(ProviderToken(provider));

        var tools = await client.ListToolsAsync();

        tools.Select(tool => tool.Name).ShouldContain("get_my_account");
    }

    // Dynamic client registration: without AllowedClientIds, a token of any client of the issuer is accepted.
    [Theory]
    [InlineData("AuthKit")]
    [InlineData("Keycloak")]
    public async Task IssuerProvider_WithoutAllowedClients_AcceptsAnyClient(string provider)
    {
        using var host = new McpTestHost(new Dictionary<string, string?> { ["Mcp:Provider"] = provider });
        var token = provider == "AuthKit"
            ? TestTokens.CreateAuthKit(overrides: new Dictionary<string, object?> { ["client_id"] = TestTokens.OtherAuthKitClientId })
            : TestTokens.CreateKeycloak(overrides: new Dictionary<string, object?> { ["azp"] = TestTokens.OtherKeycloakClientId });
        await using var client = await host.ConnectAsync(token);

        var tools = await client.ListToolsAsync();

        tools.ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidProviderTokens))]
    public async Task IssuerProvider_InvalidToken_Answers401(string provider, string because, string token)
    {
        using var host = ProviderHost(provider);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(McpRequest(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, because);
        response.Headers.WwwAuthenticate.ToString().ShouldBe($"Bearer error=\"invalid_token\", resource_metadata=\"{MetadataUrl}\"", because);
    }

    [Theory]
    [InlineData("AuthKit")]
    [InlineData("Keycloak")]
    public async Task IssuerProvider_TokenWithoutTheScope_Answers403WithTheScope(string provider)
    {
        using var host = ProviderHost(provider);
        using var client = host.CreateClient();
        var token = provider == "AuthKit"
            ? TestTokens.CreateAuthKit(overrides: new Dictionary<string, object?> { ["permissions"] = new[] { "widgets:read" } })
            : TestTokens.CreateKeycloak(overrides: new Dictionary<string, object?> { ["scope"] = "openid profile email" });

        using var response = await client.SendAsync(McpRequest(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ToString().ShouldBe(
            $"Bearer error=\"insufficient_scope\", scope=\"{TestTokens.Scope}\", resource_metadata=\"{MetadataUrl}\"");
    }

    // AuthKit tokens may name the scope in scope instead of a permission.
    [Fact]
    public async Task AuthKit_ScopeInsteadOfAPermission_IsAccepted()
    {
        using var host = ProviderHost("AuthKit");
        await using var client = await host.ConnectAsync(TestTokens.CreateAuthKit(
            overrides: new Dictionary<string, object?> { ["permissions"] = null, ["scope"] = $"openid {TestTokens.Scope}" }));

        var tools = await client.ListToolsAsync();

        tools.ShouldNotBeEmpty();
    }

    // AuthKit and Keycloak publish their own metadata: the resource names them, and the host serves no RFC 8414 document.
    [Theory]
    [InlineData("AuthKit", TestTokens.AuthKitIssuer)]
    [InlineData("Keycloak", TestTokens.KeycloakIssuer)]
    public async Task IssuerProvider_ProtectedResourceMetadata_NamesTheIssuer(string provider, string issuer)
    {
        using var host = ProviderHost(provider);
        using var client = host.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-protected-resource/mcp"));
        var root = document.RootElement;

        root.GetProperty("resource").GetString().ShouldBe(TestTokens.ResourceUrl);
        root.GetProperty("authorization_servers").EnumerateArray().Select(server => server.GetString()).ShouldBe([issuer]);
        root.GetProperty("scopes_supported").EnumerateArray().Select(scope => scope.GetString()).ShouldBe([TestTokens.Scope]);
    }

    [Theory]
    [InlineData("AuthKit")]
    [InlineData("Keycloak")]
    public async Task IssuerProvider_AuthorizationServerMetadata_IsNotServed(string provider)
    {
        using var host = ProviderHost(provider);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/.well-known/oauth-authorization-server");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    // A host of the provider that lists the test client, so a token of another client is refused.
    private static McpTestHost ProviderHost(string provider)
    {
        return new McpTestHost(new Dictionary<string, string?>
        {
            ["Mcp:Provider"] = provider,
            ["Mcp:Authentication:AllowedClientIds:0"] = provider == "AuthKit" ? TestTokens.AuthKitClientId : TestTokens.KeycloakClientId,
        });
    }

    private static string ProviderToken(string provider)
    {
        return provider == "AuthKit" ? TestTokens.CreateAuthKit() : TestTokens.CreateKeycloak();
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
