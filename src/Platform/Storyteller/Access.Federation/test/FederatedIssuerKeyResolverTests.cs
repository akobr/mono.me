using System.Net;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class FederatedIssuerKeyResolverTests : IDisposable
{
    private const string IssuerUrl = "https://cluster.example";
    private const string DiscoveryUrl = "https://cluster.example/.well-known/openid-configuration";
    private const string JwksUrl = "https://keys.cluster.example/openid/v1/jwks";

    private readonly ManualTimeProvider _time = new();
    private readonly StubHttpMessageHandler _http = new();
    private readonly RecordingLogger<FederatedIssuerKeyResolver> _logger = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:test", Audience = "urn:storyteller:test:api" };
    private readonly TestSigningKey _key = TestSigningKey.Rsa("key-1");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FederatedIssuerKeyResolver _resolver;

    public FederatedIssuerKeyResolverTests()
    {
        _resolver = new FederatedIssuerKeyResolver(
            new StubHttpClientFactory(_http),
            _cache,
            Options.Create(_options),
            _time,
            _logger);
    }

    public void Dispose()
    {
        _key.Dispose();
        _http.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task Discovery_FetchesTheMetadataThenItsKeySet()
    {
        SetDiscovery(IssuerUrl, JwksUrl);

        var keys = await _resolver.GetKeysAsync("org1", "proj1", Discovered());

        keys.ShouldNotBeNull();
        keys.IsStatic.ShouldBeFalse();
        keys.Keys.Select(key => key.KeyId).ShouldBe(["key-1"]);
        _http.Count(DiscoveryUrl).ShouldBe(1);
        _http.Count(JwksUrl).ShouldBe(1);
    }

    [Fact]
    public async Task Discovery_OfAnIssuerWithATrailingSlash_KeepsTheSlashInTheComparison()
    {
        // Entra v1 and AKS issuers end with '/'; the well-known path is appended once.
        const string issuer = "https://sts.windows.net/tenant-id/";
        _http.SetJson("https://sts.windows.net/tenant-id/.well-known/openid-configuration", Discovery(issuer, JwksUrl));
        _http.SetJson(JwksUrl, TestSigningKey.Jwks(_key));

        (await _resolver.GetKeysAsync("org1", "proj1", Discovered(issuer))).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("https://cluster.example/")]
    [InlineData("https://attacker.example")]
    [InlineData(null)]
    public async Task Discovery_NamingAnotherIssuer_IsAMisconfiguration_AndFetchesNoKeys(string? documentIssuer)
    {
        SetDiscovery(documentIssuer, JwksUrl);

        (await _resolver.GetKeysAsync("org1", "proj1", Discovered())).ShouldBeNull();

        _http.Count(JwksUrl).ShouldBe(0);
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("misconfigured"));
    }

    [Theory]
    [InlineData("http://keys.cluster.example/jwks")]
    [InlineData("https://127.0.0.1/jwks")]
    [InlineData("https://2130706433/jwks")]
    [InlineData("https://keys.cluster.example:8443/jwks")]
    [InlineData("https://user@keys.cluster.example/jwks")]
    [InlineData("/openid/v1/jwks")]
    [InlineData(null)]
    public async Task Discovery_WithAJwksUriBreakingTheUrlRules_IsAMisconfiguration(string? jwksUri)
    {
        SetDiscovery(IssuerUrl, jwksUri);

        (await _resolver.GetKeysAsync("org1", "proj1", Discovered())).ShouldBeNull();

        _http.TotalRequests.ShouldBe(1);
    }

    [Fact]
    public async Task AllowPrivateNetworks_LiftsTheUrlRules()
    {
        _options.AllowPrivateNetworks = true;
        const string issuer = "http://localhost:8080/realms/test";
        _http.SetJson($"{issuer}/.well-known/openid-configuration", Discovery(issuer, "http://127.0.0.1:8080/jwks"));
        _http.SetJson("http://127.0.0.1:8080/jwks", TestSigningKey.Jwks(_key));

        (await _resolver.GetKeysAsync("org1", "proj1", Discovered(issuer))).ShouldNotBeNull();
    }

    [Fact]
    public async Task JwksUri_FetchesOnlyTheKeySet()
    {
        _http.SetJson(JwksUrl, TestSigningKey.Jwks(_key));
        var issuer = new FederatedIssuer { Name = "cluster", Issuer = "https://kubernetes.default.svc.cluster.local", KeySource = FederatedIssuerKeySource.JwksUri, JwksUri = JwksUrl };

        var keys = await _resolver.GetKeysAsync("org1", "proj1", issuer);

        keys.ShouldNotBeNull();
        keys.Keys.Count.ShouldBe(1);
        _http.TotalRequests.ShouldBe(1);
    }

    [Fact]
    public async Task StoredJwks_FetchesNothing()
    {
        using var ec = TestSigningKey.EllipticCurve("key-2");
        var issuer = Stored(TestSigningKey.Jwks(_key, ec));

        var keys = await _resolver.GetKeysAsync("org1", "proj1", issuer);

        keys.ShouldNotBeNull();
        keys.IsStatic.ShouldBeTrue();
        keys.Keys.Select(key => key.KeyId).ShouldBe(["key-1", "key-2"]);
        _http.TotalRequests.ShouldBe(0);
    }

    [Fact]
    public async Task StoredJwksThatDoesNotParse_IsAMisconfiguration()
    {
        (await _resolver.GetKeysAsync("org1", "proj1", Stored("{\"keys\": 7}"))).ShouldBeNull();
        (await _resolver.GetKeysAsync("org1", "proj1", Stored("not json"))).ShouldBeNull();
    }

    [Fact]
    public async Task KeySetWithoutSignatureKeys_KeepsOnlyRsaAndEcSignatureKeys()
    {
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new object[]
            {
                new { kty = "oct", kid = "secret", k = "c2VjcmV0" },
                new { kty = "RSA", kid = "encryption", use = "enc", n = "AQAB", e = "AQAB" },
            },
        });
        _http.SetJson(JwksUrl, jwks);

        var keys = await _resolver.GetKeysAsync("org1", "proj1", new FederatedIssuer { Name = "cluster", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.JwksUri, JwksUri = JwksUrl });

        keys.ShouldNotBeNull();
        keys.Keys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keys_AreCachedPerIssuerSettings()
    {
        SetDiscovery(IssuerUrl, JwksUrl);
        var issuer = Discovered();

        await _resolver.GetKeysAsync("org1", "proj1", issuer);
        await _resolver.GetKeysAsync("org1", "proj1", issuer with { Description = "only the description changed" });
        _http.Count(JwksUrl).ShouldBe(1);

        // Another project, or the same name with other key settings, is another entry.
        await _resolver.GetKeysAsync("org1", "proj2", issuer);
        _http.SetJson("https://keys.cluster.example/other", TestSigningKey.Jwks(_key));
        await _resolver.GetKeysAsync("org1", "proj1", issuer with { KeySource = FederatedIssuerKeySource.JwksUri, JwksUri = "https://keys.cluster.example/other" });

        _http.Count(JwksUrl).ShouldBe(2);
        _http.Count("https://keys.cluster.example/other").ShouldBe(1);
    }

    [Fact]
    public async Task Keys_OlderThanTheRefreshAge_AreFetchedAgain()
    {
        SetDiscovery(IssuerUrl, JwksUrl);

        await _resolver.GetKeysAsync("org1", "proj1", Discovered());
        _time.Advance(TimeSpan.FromMinutes(59));
        await _resolver.GetKeysAsync("org1", "proj1", Discovered());
        _http.Count(JwksUrl).ShouldBe(1);

        _time.Advance(TimeSpan.FromMinutes(1));
        await _resolver.GetKeysAsync("org1", "proj1", Discovered());
        _http.Count(DiscoveryUrl).ShouldBe(2);
        _http.Count(JwksUrl).ShouldBe(2);
    }

    [Fact]
    public async Task UnknownKid_WithinAMinuteOfTheFetch_IsUnavailable()
    {
        SetDiscovery(IssuerUrl, JwksUrl);
        var keys = await _resolver.GetKeysAsync("org1", "proj1", Discovered());
        _time.Advance(TimeSpan.FromSeconds(59));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.RefreshForUnknownKeyAsync("org1", "proj1", Discovered(), keys!));
        _http.Count(JwksUrl).ShouldBe(1);
    }

    [Fact]
    public async Task UnknownKid_AfterAMinute_FetchesTheKeysAgainAtOnce()
    {
        SetDiscovery(IssuerUrl, JwksUrl);
        var keys = await _resolver.GetKeysAsync("org1", "proj1", Discovered());
        _time.Advance(TimeSpan.FromMinutes(1));
        using var rotated = TestSigningKey.Rsa("key-2");
        _http.SetJson(JwksUrl, TestSigningKey.Jwks(_key, rotated));

        var refreshed = await _resolver.RefreshForUnknownKeyAsync("org1", "proj1", Discovered(), keys!);

        refreshed.ShouldNotBeNull();
        refreshed.Keys.Select(key => key.KeyId).ShouldBe(["key-1", "key-2"]);
        _http.Count(JwksUrl).ShouldBe(2);

        // A second request with the stale keys gets the ones just fetched.
        (await _resolver.RefreshForUnknownKeyAsync("org1", "proj1", Discovered(), keys!)).ShouldBeSameAs(refreshed);
        _http.Count(JwksUrl).ShouldBe(2);
    }

    [Fact]
    public async Task UnknownKid_OfAStoredKeySet_IsRefusedWithoutAFetch()
    {
        var issuer = Stored(TestSigningKey.Jwks(_key));
        var keys = await _resolver.GetKeysAsync("org1", "proj1", issuer);
        _time.Advance(TimeSpan.FromHours(2));

        (await _resolver.RefreshForUnknownKeyAsync("org1", "proj1", issuer, keys!)).ShouldBeNull();
        _http.TotalRequests.ShouldBe(0);
    }

    [Fact]
    public async Task ConcurrentRequests_ShareOneFetch()
    {
        SetDiscovery(IssuerUrl, JwksUrl);
        _http.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _resolver.GetKeysAsync("org1", "proj1", Discovered())))
            .ToList();
        await Task.Delay(100);
        _http.Gate.SetResult();
        var results = await Task.WhenAll(requests);

        results.ShouldAllBe(keys => keys != null && ReferenceEquals(keys, results[0]));
        _http.Count(DiscoveryUrl).ShouldBe(1);
        _http.Count(JwksUrl).ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Found)]
    public async Task HttpFailure_IsUnavailable(HttpStatusCode status)
    {
        _http.Set(DiscoveryUrl, () =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return response;
        });

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.GetKeysAsync("org1", "proj1", Discovered()));
        _http.TotalRequests.ShouldBe(1);
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable_AndAnsweredFromTheFailureForAFewSeconds()
    {
        _http.Set(DiscoveryUrl, () => throw new HttpRequestException("connection refused"));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.GetKeysAsync("org1", "proj1", Discovered()));
        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.GetKeysAsync("org1", "proj1", Discovered()));
        _http.Count(DiscoveryUrl).ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(5));
        SetDiscovery(IssuerUrl, JwksUrl);
        (await _resolver.GetKeysAsync("org1", "proj1", Discovered())).ShouldNotBeNull();
    }

    [Fact]
    public async Task OversizedResponse_IsUnavailable()
    {
        SetDiscovery(IssuerUrl, JwksUrl);
        _http.SetJson(JwksUrl, $"{{\"keys\": [], \"padding\": \"{new string('x', 600 * 1024)}\"}}");

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.GetKeysAsync("org1", "proj1", Discovered()));
    }

    [Fact]
    public async Task OversizedResponseWithoutContentLength_IsUnavailable()
    {
        SetDiscovery(IssuerUrl, JwksUrl);
        _http.Set(JwksUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnsizedStream(new byte[600 * 1024])),
        });

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.GetKeysAsync("org1", "proj1", Discovered()));
    }

    [Fact]
    public async Task ResponseBodyThatStalls_IsUnavailableAfterTheFetchTimeout()
    {
        // The headers arrive at once and the body never does: the timeout covers the whole fetch, not only the headers.
        var resolver = new FederatedIssuerKeyResolver(new StubHttpClientFactory(_http), _cache, Options.Create(_options), _time, _logger)
        {
            FetchTimeout = TimeSpan.FromMilliseconds(200),
        };
        _http.Set(DiscoveryUrl, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => resolver.GetKeysAsync("org1", "proj1", Discovered()).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("[1, 2]")]
    public async Task DiscoveryDocumentThatIsNotAJsonObject_IsUnavailable(string content)
    {
        _http.SetJson(DiscoveryUrl, content);

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _resolver.GetKeysAsync("org1", "proj1", Discovered()));
    }

    private static FederatedIssuer Discovered(string issuer = IssuerUrl)
    {
        return new FederatedIssuer { Name = "cluster", Issuer = issuer, KeySource = FederatedIssuerKeySource.Discovery };
    }

    private static FederatedIssuer Stored(string jwks)
    {
        return new FederatedIssuer { Name = "cluster", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.Jwks, Jwks = jwks };
    }

    private static string Discovery(string? issuer, string? jwksUri)
    {
        return JsonSerializer.Serialize(new { issuer, jwks_uri = jwksUri, id_token_signing_alg_values_supported = new[] { "RS256" } });
    }

    private void SetDiscovery(string? issuer, string? jwksUri)
    {
        _http.SetJson(DiscoveryUrl, Discovery(issuer, jwksUri));
        _http.SetJson(JwksUrl, TestSigningKey.Jwks(_key));
    }

    // A stream that does not report its length, so the response has no Content-Length.
    private sealed class UnsizedStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }

    // A response body whose reads wait until they are cancelled, like a host that sent its headers and then stopped.
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
