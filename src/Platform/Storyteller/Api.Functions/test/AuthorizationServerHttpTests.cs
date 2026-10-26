using System.Net;
using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using static _42.Platform.Storyteller.Api.Functions.UnitTests.OAuthHttpTesting;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class AuthorizationServerHttpTests
{
    private const string MetadataUrl = "https://api.example/api/.well-known/oauth-authorization-server";
    private const string JwksUrl = "https://api.example/api/.well-known/jwks.json";

    private readonly FederationOptions _options = new() { Issuer = "https://storyteller.example", Audience = "https://storyteller.example/api" };
    private readonly InMemoryTokenSigningKeyStore _store = new();

    // RFC 8414 metadata
    [Fact]
    public async Task Metadata_NamesTheIssuerTheUserTokenEndpointAndTheJwks()
    {
        var response = await CreateHttp().GetAuthorizationServerMetadata(Get(MetadataUrl));

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK);
        PropertyNames(body).ShouldBe(
            [
                "issuer",
                "token_endpoint",
                "jwks_uri",
                "response_types_supported",
                "grant_types_supported",
                "token_endpoint_auth_methods_supported",
                "token_endpoint_auth_signing_alg_values_supported",
            ],
            ignoreOrder: true);
        body.GetProperty("issuer").GetString().ShouldBe("https://storyteller.example");
        body.GetProperty("token_endpoint").GetString().ShouldBe("https://api.example/api/v1/access/token");
        body.GetProperty("jwks_uri").GetString().ShouldBe("https://api.example/api/.well-known/jwks.json");
        body.GetProperty("response_types_supported").GetArrayLength().ShouldBe(0, "no authorization endpoint");
        Strings(body.GetProperty("grant_types_supported")).ShouldBe(["urn:ietf:params:oauth:grant-type:token-exchange"]);
        Strings(body.GetProperty("token_endpoint_auth_methods_supported")).ShouldBe(["private_key_jwt"]);
        Strings(body.GetProperty("token_endpoint_auth_signing_alg_values_supported")).ShouldBe([.. SubjectTokenVerifier.AllowedAlgorithms]);
        Strings(body.GetProperty("token_endpoint_auth_signing_alg_values_supported")).ShouldAllBe(algorithm => algorithm != "none" && !algorithm!.StartsWith("HS", StringComparison.Ordinal));
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");

        // Without PublicBaseUrl its URLs come from the request's host, so no shared cache keeps them.
        Header(response, "Cache-Control").ShouldBe("no-store");
    }

    // A client secret is a proof of local development only.
    [Fact]
    public async Task Metadata_InDevelopment_AlsoNamesClientSecretPost()
    {
        var response = await CreateHttp(environment: Environments.Development).GetAuthorizationServerMetadata(Get(MetadataUrl));

        var (_, body) = await ReadAsync(response);
        Strings(body.GetProperty("token_endpoint_auth_methods_supported")).ShouldBe(["private_key_jwt", "client_secret_post"]);
    }

    [Theory]
    [InlineData("https://storyteller.example/api")]
    [InlineData("https://storyteller.example/api/")]
    public async Task Metadata_WithPublicBaseUrl_BuildsTheUrlsFromIt(string publicBaseUrl)
    {
        _options.PublicBaseUrl = publicBaseUrl;

        var response = await CreateHttp().GetAuthorizationServerMetadata(Get(MetadataUrl));

        var (_, body) = await ReadAsync(response);
        body.GetProperty("token_endpoint").GetString().ShouldBe("https://storyteller.example/api/v1/access/token");
        body.GetProperty("jwks_uri").GetString().ShouldBe("https://storyteller.example/api/.well-known/jwks.json");
        body.GetProperty("issuer").GetString().ShouldBe("https://storyteller.example");
        Header(response, "Cache-Control").ShouldBe("public, max-age=300");
    }

    [Fact]
    public async Task Metadata_WithoutPublicBaseUrl_UsesTheRequestSchemeAndHost_NeverAForwardedHost()
    {
        var request = Get("https://api.example:8443/api/.well-known/oauth-authorization-server", new Dictionary<string, string> { ["X-Forwarded-Host"] = "evil.example" });

        var response = await CreateHttp().GetAuthorizationServerMetadata(request);

        var (_, body) = await ReadAsync(response);
        body.GetProperty("token_endpoint").GetString().ShouldBe("https://api.example:8443/api/v1/access/token");
        body.GetProperty("jwks_uri").GetString().ShouldBe("https://api.example:8443/api/.well-known/jwks.json");
        Header(response, "Cache-Control").ShouldBe("no-store");
    }

    // The JWKS
    [Fact]
    public async Task Jwks_PublishesThePublicPartOfTheSigningKey()
    {
        var keyProvider = CreateKeyProvider(_store);
        var credentials = await keyProvider.GetSigningCredentialsAsync();

        var response = await CreateHttp(keyProvider).GetJsonWebKeySet(Get(JwksUrl));

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK);
        PropertyNames(body).ShouldBe(["keys"]);
        var key = body.GetProperty("keys").EnumerateArray().ShouldHaveSingleItem();
        PropertyNames(key).ShouldBe(["kty", "kid", "use", "alg", "n", "e"], ignoreOrder: true, "the public members only, never d, p, q, dp, dq or qi");
        key.GetProperty("kty").GetString().ShouldBe("RSA");
        key.GetProperty("kid").GetString().ShouldBe(credentials.Key.KeyId);
        key.GetProperty("use").GetString().ShouldBe("sig");
        key.GetProperty("alg").GetString().ShouldBe("RS256");

        var stored = (await _store.GetAsync())!;
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(stored.PrivateKeyPkcs8, out _);
        var publicParameters = rsa.ExportParameters(includePrivateParameters: false);
        key.GetProperty("n").GetString().ShouldBe(Base64UrlEncoder.Encode(publicParameters.Modulus));
        key.GetProperty("e").GetString().ShouldBe(Base64UrlEncoder.Encode(publicParameters.Exponent));
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("public, max-age=300");
    }

    // The anonymous JWKS never creates a key, and while there is none it is not cached: the first issued token creates it.
    [Fact]
    public async Task Jwks_WithoutAKey_IsEmptyAndNotCached_AndCreatesNone()
    {
        var response = await CreateHttp().GetJsonWebKeySet(Get(JwksUrl));

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("keys").GetArrayLength().ShouldBe(0);
        Header(response, "Cache-Control").ShouldBe("no-cache");
        (await _store.GetAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Jwks_StoreUnavailable_IsServiceUnavailableWithRetryAfter()
    {
        var response = await CreateHttp(CreateKeyProvider(new FailingSigningKeyStore())).GetJsonWebKeySet(Get(JwksUrl));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        Header(response, "Retry-After").ShouldBe("5");
        Header(response, "Cache-Control").ShouldBe("no-store");
        response.Body.Length.ShouldBe(0);
    }

    private AuthorizationServerHttp CreateHttp(TokenSigningKeyProvider? keyProvider = null, string environment = "Production")
    {
        return new AuthorizationServerHttp(
            keyProvider ?? CreateKeyProvider(_store),
            Options.Create(_options),
            new TestHostEnvironment(environment),
            NullLogger<AuthorizationServerHttp>.Instance);
    }

    private TokenSigningKeyProvider CreateKeyProvider(ITokenSigningKeyStore store)
    {
        return new TokenSigningKeyProvider(store, Options.Create(_options), TimeProvider.System, NullLogger<TokenSigningKeyProvider>.Instance);
    }

    private sealed class FailingSigningKeyStore : ITokenSigningKeyStore
    {
        public Task<TokenSigningKey?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<TokenSigningKey?>(new InvalidOperationException("The store is down."));
        }

        public Task<bool> TryCreateAsync(TokenSigningKey key, CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new InvalidOperationException("The store is down."));
        }
    }
}
