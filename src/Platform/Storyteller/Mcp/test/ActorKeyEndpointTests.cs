using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The JWKS of the actor's client assertion key, which the API's ClientAssertion actor reads: anonymous, public keys only.
public sealed class ActorKeyEndpointTests : IDisposable
{
    private const string CertificatePath = "/etc/mcp/actor.pfx";

    private readonly List<McpTestHost> _hosts = [];

    [Fact]
    public async Task WithAClientAssertionKey_ServesItsPublicKey_Anonymously()
    {
        var host = KeyHost();
        host.Files.AddFile(CertificatePath, new MockFileData(TestCertificates.RsaPkcs12));
        using var client = host.CreateClient();

        using var response = await client.GetAsync(McpActorKey.JwksPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        response.Headers.CacheControl?.ToString().ShouldBe(ActorKeyEndpoints.PublicCacheControl);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = document.RootElement.GetProperty("keys").EnumerateArray().ShouldHaveSingleItem();
        key.EnumerateObject().Select(property => property.Name).Order().ShouldBe(["alg", "e", "kid", "kty", "n", "use"]);
        key.GetProperty("kty").GetString().ShouldBe("RSA");
        key.GetProperty("use").GetString().ShouldBe("sig");
        key.GetProperty("alg").GetString().ShouldBe("RS256");
        key.GetProperty("n").GetString().ShouldBe(Base64UrlEncoder.Encode(TestCertificates.RsaPublicKey.Modulus));
        key.GetProperty("e").GetString().ShouldBe(Base64UrlEncoder.Encode(TestCertificates.RsaPublicKey.Exponent));
        key.GetProperty("kid").GetString().ShouldBe(McpActorKey.ComputeKeyId(TestCertificates.RsaPublicKey));
    }

    // The API finds the assertion's key in the JWKS by its kid and checks the signature with it.
    [Fact]
    public async Task ClientAssertion_IsVerifiedWithThePublishedKey()
    {
        var host = KeyHost();
        host.Files.AddFile(CertificatePath, new MockFileData(TestCertificates.RsaPkcs12));
        using var client = host.CreateClient();
        var keys = new JsonWebKeySet(await client.GetStringAsync(McpActorKey.JwksPath));

        var assertion = host.Services.GetRequiredService<McpActorKey>().CreateClientAssertion("mcp", "https://api.test/api/v1/access/token", DateTimeOffset.UtcNow);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(assertion, new TokenValidationParameters
        {
            IssuerSigningKeys = keys.GetSigningKeys(),
            TryAllIssuerSigningKeys = false,
            ValidIssuer = "mcp",
            ValidAudience = "https://api.test/api/v1/access/token",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });

        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
    }

    [Fact]
    public async Task WithoutAClientAssertionKey_IsNotFound()
    {
        var host = new McpTestHost();
        _hosts.Add(host);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(McpActorKey.JwksPath);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Anonymous requests cannot make the server read and decrypt the file, and log, on every request: after a failed
    // read the next one waits for McpActorKey.RetryDelay, which the answer names as Retry-After.
    [Fact]
    public async Task UnreadableKey_IsUnavailable_ForTheRetryDelay()
    {
        const string Unreadable = "The client assertion key of Mcp:Actor could not be read (FileNotFoundException).";
        var host = KeyHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(McpActorKey.JwksPath);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(McpActorKey.RetryDelay);
        host.Logs.Entries.ShouldContain(entry => entry.Contains(Unreadable, StringComparison.Ordinal));

        // The file appears within the delay: no new read yet.
        host.Files.AddFile(CertificatePath, new MockFileData(TestCertificates.RsaPkcs12));
        using var retried = await client.GetAsync(McpActorKey.JwksPath);
        retried.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        host.Logs.Entries.Count(entry => entry.Contains("could not be read", StringComparison.Ordinal)).ShouldBe(1);
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    // A host whose actor signs client assertions instead of sending the default token file.
    private McpTestHost KeyHost()
    {
        var host = new McpTestHost(new Dictionary<string, string?>
        {
            ["Mcp:Actor:WorkloadIdentity:Kind"] = null,
            ["Mcp:Actor:WorkloadIdentity:TokenFilePath"] = null,
            ["Mcp:Actor:ClientAssertionKey:CertificatePath"] = CertificatePath,
            ["Mcp:Actor:ClientAssertionKey:CertificatePassword"] = TestCertificates.Password,
        });
        _hosts.Add(host);
        return host;
    }
}
