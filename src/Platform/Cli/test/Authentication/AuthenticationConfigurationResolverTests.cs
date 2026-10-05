using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public sealed class AuthenticationConfigurationResolverTests : IDisposable
{
    private const string BaseUrl = "https://storyteller.example/api";

    private readonly ScriptedHttpHandler _http = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sform-auth-" + Guid.NewGuid().ToString("N"));

    public AuthenticationConfigurationResolverTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private string CachePath => Path.Combine(_directory, Constants.AUTH_DISCOVERY_JSON);

    [Fact]
    public async Task ConfiguredProvider_IsUsedWithoutDiscovery()
    {
        var resolved = await CreateResolver(new AuthenticationOptions
        {
            Provider = "authkit",
            ClientId = "client_123",
            AuthKitApiBaseUrl = "https://workos.example",
        }).ResolveAsync();

        resolved.Provider.ShouldBe(AuthenticationProvider.AuthKit);
        resolved.ClientId.ShouldBe("client_123");
        resolved.AuthKitApiBaseUrl.ShouldBe("https://workos.example");
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Okta", "client")]
    [InlineData("AuthKit", null)]
    public async Task InvalidConfiguredProvider_Throws(string provider, string? clientId)
    {
        var resolver = CreateResolver(new AuthenticationOptions { Provider = provider, ClientId = clientId });

        await Should.ThrowAsync<AuthenticationException>(() => resolver.ResolveAsync());
    }

    [Fact]
    public async Task NoProvider_DiscoversAnonymouslyAndCachesTheAnswer()
    {
        _http.Respond(HttpStatusCode.OK, """{"Provider":"AuthKit","ClientId":"client_123","AuthKitDomain":"https://example.authkit.app"}""");

        var resolved = await CreateResolver(new AuthenticationOptions()).ResolveAsync();

        resolved.Provider.ShouldBe(AuthenticationProvider.AuthKit);
        resolved.ClientId.ShouldBe("client_123");
        resolved.AuthKitApiBaseUrl.ShouldBe(AuthKitDefaults.ApiBaseUrl);
        resolved.AuthKitDomain.ShouldBe("https://example.authkit.app");
        _http.Requests.Count.ShouldBe(1);
        _http.Requests[0].Uri.ShouldBe(new Uri($"{BaseUrl}/v1/auth/configuration"));
        _http.Requests[0].Authorization.ShouldBeNull();

        var cached = JsonSerializer.Deserialize<AuthenticationDiscoveryCache>(File.ReadAllText(CachePath));
        cached.ShouldNotBeNull();
        cached.BaseUrl.ShouldBe(BaseUrl);
        cached.Provider.ShouldBe("AuthKit");
        cached.AuthKitDomain.ShouldBe("https://example.authkit.app");

        var again = await CreateResolver(new AuthenticationOptions()).ResolveAsync();
        again.ClientId.ShouldBe("client_123");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EntraDiscovery_CarriesTenantAndScopes()
    {
        _http.Respond(HttpStatusCode.OK, """{"Provider":"EntraId","ClientId":"app-id","TenantId":"common","Scopes":["api://app-id/User.Impersonation"]}""");

        var resolved = await CreateResolver(new AuthenticationOptions()).ResolveAsync();

        resolved.Provider.ShouldBe(AuthenticationProvider.EntraId);
        resolved.TenantId.ShouldBe("common");
        resolved.Scopes.ShouldBe(["api://app-id/User.Impersonation"]);
    }

    [Fact]
    public async Task CacheWriteFailure_StillReturnsTheDiscoveredSettings()
    {
        // The cache path is a directory, so WriteAllText is denied and must not fail discovery.
        Directory.CreateDirectory(CachePath);
        _http.Respond(HttpStatusCode.OK, """{"Provider":"AuthKit","ClientId":"client_123"}""");

        var resolved = await CreateResolver(new AuthenticationOptions()).ResolveAsync();

        resolved.Provider.ShouldBe(AuthenticationProvider.AuthKit);
        resolved.ClientId.ShouldBe("client_123");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CacheForAnotherServer_IsIgnored()
    {
        File.WriteAllText(CachePath, """{"baseUrl":"https://other.example/api","provider":"EntraId","clientId":"old"}""");
        _http.Respond(HttpStatusCode.OK, """{"Provider":"AuthKit","ClientId":"client_123"}""");

        var resolved = await CreateResolver(new AuthenticationOptions()).ResolveAsync();

        resolved.ClientId.ShouldBe("client_123");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ServerWithoutDiscovery_FallsBackToTheEntraSettings()
    {
        _http.Respond(HttpStatusCode.NotFound, string.Empty);

        var resolved = await CreateResolver(new AuthenticationOptions { TenantId = "common", ClientId = "app-id" }).ResolveAsync();

        resolved.Provider.ShouldBe(AuthenticationProvider.EntraId);
        resolved.ClientId.ShouldBe("app-id");
        resolved.TenantId.ShouldBe("common");
        File.Exists(CachePath).ShouldBeFalse();
    }

    [Fact]
    public async Task ServerWithoutDiscoveryAndNoSettings_Throws()
    {
        _http.Respond(HttpStatusCode.NotFound, string.Empty);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateResolver(new AuthenticationOptions()).ResolveAsync());

        exception.Message.ShouldContain(BaseUrl);
    }

    [Fact]
    public async Task Forget_DeletesTheCache()
    {
        File.WriteAllText(CachePath, "{}");

        await CreateResolver(new AuthenticationOptions()).ForgetAsync();

        File.Exists(CachePath).ShouldBeFalse();
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private AuthenticationConfigurationResolver CreateResolver(AuthenticationOptions options)
    {
        return new AuthenticationConfigurationResolver(
            Options.Create(options),
            Options.Create(new GeneralOptions { BaseUrl = BaseUrl + "/" }),
            new StubHttpClientFactory(_http),
            new FileSystem(),
            NullLogger<AuthenticationConfigurationResolver>.Instance,
            _directory);
    }
}
