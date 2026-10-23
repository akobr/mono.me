using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Identity.Client;

namespace _42.Platform.Storyteller.Sdk.Configuring.KeyVault.UnitTests;

public class KeyVaultReferenceResolverTests
{
    private const string Version = "0123456789abcdef0123456789abcdef";
    private const string SecretValue = "s3cr3t-value-text";
    private const string TenantId = "00000000-0000-0000-0000-00000000c0de";

    private readonly List<StubSecretClient> _clients = [];

    private Func<Uri, string, string?, CancellationToken, Task<KeyVaultSecret>> _respond =
        (_, name, version, _) => Task.FromResult(StubSecretClient.Secret(name, version, SecretValue));

    public enum Failure
    {
        Network,
        Timeout,
        Io,
        Unavailable,
        Forbidden,
        NetworkThenNotFound,
    }

    public enum TokenFailure
    {
        Network,
        Unavailable,
        Throttled,
        InvalidRequest,
        InvalidClient,
    }

    [Theory]
    [InlineData("kv://my-vault/db-password", "https://my-vault.vault.azure.net/", "db-password", null)]
    [InlineData("  kv://My-Vault/db-password/0123456789abcdef0123456789ABCDEF  ", "https://my-vault.vault.azure.net/", "db-password", "0123456789abcdef0123456789ABCDEF")]
    [InlineData("https://other.vault.azure.net/secrets/name/", "https://other.vault.azure.net/", "name", null)]
    [InlineData("https://Other.Vault.Azure.Net/secrets/Name/" + Version, "https://other.vault.azure.net/", "Name", Version)]
    [InlineData("""{"uri":"https://other.vault.azure.net/secrets/name/0123456789abcdef0123456789abcdef"}""", "https://other.vault.azure.net/", "name", Version)]
    public async Task AReference_IsReadFromItsVault_WithItsNameAndVersion(string value, string vaultUri, string name, string? version)
    {
        var resolver = Create();

        resolver.CanResolve(value).ShouldBeTrue();
        (await resolver.ResolveAsync(value, CancellationToken.None)).ShouldBe(SecretValue);

        var client = _clients.ShouldHaveSingleItem();
        client.VaultUri.ShouldBe(new Uri(vaultUri));
        var read = client.Reads.ShouldHaveSingleItem();
        read.Name.ShouldBe(name);
        read.Version.ShouldBe(version);
    }

    [Fact]
    public async Task TheShorthand_ExpandsToTheDefaultVaultSuffix_WhichIsAlwaysAllowed()
    {
        var resolver = Create(options => options.DefaultVaultSuffix = ".Vault.Azure.CN");

        await resolver.ResolveAsync("kv://my-vault/db-password", CancellationToken.None);

        _clients.ShouldHaveSingleItem().VaultUri.ShouldBe(new Uri("https://my-vault.vault.azure.cn/"));
        resolver.CanResolve("https://my-vault.vault.azure.cn/secrets/db-password").ShouldBeTrue();
        resolver.CanResolve("https://my-vault.vault.azure.net/secrets/db-password").ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://myvault.vault.azure.net/secrets/x", true)]
    [InlineData("https://MYVAULT.VAULT.AZURE.NET/secrets/x", true)]
    [InlineData("https://vault.azure.net/secrets/x", true)]
    [InlineData("https://evilvault.azure.net/secrets/x", false)]
    [InlineData("https://myvaultvault.azure.net/secrets/x", false)]
    [InlineData("https://myvault.vault.azure.net.evil.example/secrets/x", false)]
    [InlineData("https://myvault.vault.azure.cn/secrets/x", false)]
    [InlineData("https://localhost/secrets/x", false)]
    public void TheHostOfASecretUri_MatchesASuffixOnADotBoundary(string value, bool accepted)
    {
        Create().CanResolve(value).ShouldBe(accepted);
    }

    [Theory]
    [InlineData("https://myvault.vault.azure.cn/secrets/x", true)]
    [InlineData("https://localhost/secrets/x", true)]
    [InlineData("https://kv.bücher.example/secrets/x", true)]
    [InlineData("https://kv.xn--bcher-kva.example/secrets/x", true)]
    [InlineData("https://kv.BÜCHER.example/secrets/x", true)]
    [InlineData("https://kvbücher.example/secrets/x", false)]
    [InlineData("https://kv.bücher.example.evil/secrets/x", false)]
    public void AddedHostSuffixes_MatchTheIdnHost_WithoutCase(string value, bool accepted)
    {
        var resolver = Create(options =>
        {
            options.AllowedHostSuffixes.Add(".vault.azure.cn");
            options.AllowedHostSuffixes.Add("localhost");
            options.AllowedHostSuffixes.Add("Bücher.Example");
        });

        resolver.CanResolve(value).ShouldBe(accepted);
    }

    [Fact]
    public async Task AnIdnHost_IsReadThroughItsAsciiForm()
    {
        var resolver = Create(options => options.AllowedHostSuffixes.Add("bücher.example"));

        await resolver.ResolveAsync("https://kv.bücher.example/secrets/x", CancellationToken.None);
        await resolver.ResolveAsync("https://kv.xn--bcher-kva.example/secrets/y", CancellationToken.None);

        _clients.ShouldHaveSingleItem().VaultUri.ShouldBe(new Uri("https://kv.xn--bcher-kva.example/"));
    }

    [Theory]
    [InlineData("kv://myvault/x", true)]
    [InlineData("kv://MyVault/x", true)]
    [InlineData("https://myvault.vault.azure.net/secrets/x", true)]
    [InlineData("""{"uri":"https://MYVAULT.vault.azure.net/secrets/x"}""", true)]
    [InlineData("kv://other/x", false)]
    [InlineData("https://other.vault.azure.net/secrets/x", false)]
    [InlineData("https://myvault-2.vault.azure.net/secrets/x", false)]
    public void AllowedVaults_DeclineEveryOtherVault(string value, bool accepted)
    {
        var resolver = Create(options => options.AllowedVaults.Add(" myvault "));

        resolver.CanResolve(value).ShouldBe(accepted);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("@kv.db-password")]
    [InlineData("Server=x;Password=kv://my-vault/db-password")]
    [InlineData("kv://ab/name")]
    [InlineData("http://my-vault.vault.azure.net/secrets/name")]
    [InlineData("https://my-vault.vault.azure.net:443/secrets/name")]
    [InlineData("https://my-vault.vault.azure.net/keys/name")]
    public async Task OtherValues_AreDeclined(string value)
    {
        var resolver = Create();

        resolver.CanResolve(value).ShouldBeFalse();

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => resolver.ResolveAsync(value, CancellationToken.None).AsTask());
        exception.IsTransient.ShouldBeFalse();
        _clients.ShouldBeEmpty();
    }

    [Fact]
    public async Task OneClient_ServesEveryReferenceOfItsVault()
    {
        var resolver = Create();

        await resolver.ResolveAsync("kv://my-vault/a", CancellationToken.None);
        await resolver.ResolveAsync("https://MY-VAULT.vault.azure.net/secrets/b", CancellationToken.None);
        await resolver.ResolveAsync("""{"uri":"https://my-vault.vault.azure.net/secrets/c"}""", CancellationToken.None);
        await resolver.ResolveAsync("kv://other-vault/a", CancellationToken.None);

        _clients.Count.ShouldBe(2);
        _clients[0].VaultUri.ShouldBe(new Uri("https://my-vault.vault.azure.net/"));
        _clients[0].Reads.Select(read => read.Name).ShouldBe(["a", "b", "c"]);
        _clients[1].VaultUri.ShouldBe(new Uri("https://other-vault.vault.azure.net/"));
    }

    [Fact]
    public async Task TheCallersToken_ReachesTheRead_AndItsCancellationComesOut()
    {
        _respond = async (_, name, version, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return StubSecretClient.Secret(name, version, SecretValue);
        };
        var resolver = Create();
        using var cancellation = new CancellationTokenSource();

        var resolving = resolver.ResolveAsync("kv://my-vault/db-password", cancellation.Token).AsTask();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => resolving);
        _clients.ShouldHaveSingleItem().Reads.ShouldHaveSingleItem().CancellationToken.ShouldBe(cancellation.Token);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(409, false)]
    public async Task AFailedRead_KeepsItsClass(int status, bool isTransient)
    {
        _respond = (_, _, _, _) => throw new RequestFailedException(status, "Key Vault said no.", "SomeCode", null);

        var exception = await Resolve("kv://my-vault/db-password");

        exception.IsTransient.ShouldBe(isTransient);
        exception.VaultName.ShouldBe("my-vault");
        exception.SecretName.ShouldBe("db-password");
        exception.Message.ShouldContain("db-password");
        exception.Message.ShouldContain("my-vault.vault.azure.net");
        exception.Message.ShouldContain($"({status} SomeCode)");
        exception.InnerException.ShouldBeOfType<RequestFailedException>();

        if (status == 404)
        {
            exception.Message.ShouldBe("The vault my-vault.vault.azure.net does not hold the secret db-password (404 SomeCode).");
        }
    }

    [Fact]
    public async Task ADisabledSecret_IsDefinitive()
    {
        _respond = (_, _, _, _) => throw new RequestFailedException(403, "Operation get is not allowed on a disabled secret.", "Forbidden", null);

        var exception = await Resolve("kv://my-vault/db-password");

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("disabled");
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    public async Task OtherFailures_AreTransientForTheNetworkAndTimeouts_AndDefinitiveOtherwise(Type type, bool isTransient)
    {
        _respond = (_, _, _, _) => throw (Exception)Activator.CreateInstance(type)!;

        var exception = await Resolve("kv://my-vault/db-password");

        exception.IsTransient.ShouldBe(isTransient);
        exception.InnerException.ShouldBeOfType(type);
        exception.Message.ShouldContain(type.Name);
    }

    // A real SecretClient with its default retries (three), over a transport that fails: once the retries give up,
    // Azure.Core throws the failures of every attempt in an AggregateException.
    [Theory]
    [InlineData(Failure.Network, 4, true)]
    [InlineData(Failure.Timeout, 4, true)]
    [InlineData(Failure.Io, 4, true)]
    [InlineData(Failure.Unavailable, 4, true)]
    [InlineData(Failure.Forbidden, 1, false)]
    [InlineData(Failure.NetworkThenNotFound, 2, false)]
    public async Task ThroughTheRetriesOfARealClient_FailuresKeepTheirClass(Failure failure, int attempts, bool isTransient)
    {
        var handler = new FailingHandler((attempt, _) => failure switch
        {
            Failure.Network => throw new HttpRequestException("Connection refused."),
            Failure.Timeout => throw new TaskCanceledException("The attempt timed out."),
            Failure.Io => throw new IOException("The connection was reset."),
            Failure.Unavailable => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            Failure.Forbidden => new HttpResponseMessage(HttpStatusCode.Forbidden),
            _ => attempt == 1 ? throw new HttpRequestException("Connection refused.") : new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var exception = await Resolve("kv://my-vault/db-password", CreateWithTransport(handler));

        handler.Attempts.ShouldBe(attempts);
        exception.IsTransient.ShouldBe(isTransient);
        exception.VaultName.ShouldBe("my-vault");
        exception.SecretName.ShouldBe("db-password");
        exception.Message.ShouldContain("my-vault.vault.azure.net");

        if (failure is Failure.Network or Failure.Timeout or Failure.Io)
        {
            exception.Message.ShouldContain("after 4 attempts");
            exception.InnerException.ShouldBeOfType<AggregateException>();
        }
    }

    [Fact]
    public async Task ACancellationBetweenTheRetriesOfARealClient_ComesOutAsACancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FailingHandler((attempt, cancellationToken) =>
        {
            if (attempt > 1)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new HttpRequestException("Connection refused.");
        });
        var resolver = CreateWithTransport(handler);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => resolver.ResolveAsync("kv://my-vault/db-password", cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
        exception.ShouldNotBeAssignableTo<SecretResolutionException>();
    }

    // A real SecretClient and a real ClientSecretCredential: Key Vault answers the read with its challenge, and the token
    // request to Entra fails. Azure.Identity throws AuthenticationFailedException over MSAL's MsalServiceException for an
    // answer of Entra, and over the AggregateException of its retries for a network error.
    [Theory]
    [InlineData(TokenFailure.Network, true)]
    [InlineData(TokenFailure.Unavailable, true)]
    [InlineData(TokenFailure.Throttled, true)]
    [InlineData(TokenFailure.InvalidRequest, false)]
    [InlineData(TokenFailure.InvalidClient, false)]
    public async Task AFailedTokenRequestOfARealCredential_KeepsItsClass(TokenFailure failure, bool isTransient)
    {
        var entra = new FailingHandler((_, _) => failure switch
        {
            TokenFailure.Network => throw new HttpRequestException("Connection refused."),
            TokenFailure.Unavailable => EntraError(HttpStatusCode.ServiceUnavailable, "temporarily_unavailable"),
            TokenFailure.Throttled => EntraError(HttpStatusCode.TooManyRequests, "too_many_requests"),
            TokenFailure.InvalidRequest => EntraError(HttpStatusCode.BadRequest, "unauthorized_client"),
            _ => EntraError(HttpStatusCode.Unauthorized, "invalid_client"),
        });

        // A client of its own: MSAL throttles a client that Entra answered with 429 or 5xx, across instances.
        var credential = new ClientSecretCredential(TenantId, Guid.NewGuid().ToString(), "client-secret", new ClientSecretCredentialOptions
        {
            Transport = new HttpClientTransport(new HttpClient(entra)),
            Retry = { Mode = RetryMode.Fixed, Delay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(1) },
        });
        var vault = new FailingHandler((_, _) => KeyVaultChallenge());

        // A vault of its own: SecretClient keeps the challenge of a vault for every client in the process, and then asks
        // for a token before the first request.
        var exception = await Resolve("kv://token-vault/db-password", CreateWithTransport(vault, credential));

        entra.Attempts.ShouldBeGreaterThan(0);
        exception.IsTransient.ShouldBe(isTransient);
        exception.VaultName.ShouldBe("token-vault");
        exception.SecretName.ShouldBe("db-password");
        var authentication = exception.InnerException.ShouldBeOfType<AuthenticationFailedException>();

        if (failure == TokenFailure.Network)
        {
            authentication.InnerException.ShouldBeOfType<AggregateException>();
        }
        else
        {
            authentication.InnerException.ShouldBeAssignableTo<MsalServiceException>();
        }
    }

    // MSAL throttles a token request that Entra refused recently with 429 or 5xx itself, and repeats that answer.
    [Theory]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    public async Task AThrottledTokenRequest_KeepsTheClassOfTheAnswerItRepeats(int status, bool isTransient)
    {
        var throttled = new MsalThrottledServiceException(new MsalServiceException("request_throttled", "Entra is busy.", status));
        _respond = (_, _, _, _) => throw new AuthenticationFailedException("The credential failed.", throttled);

        (await Resolve("kv://my-vault/db-password")).IsTransient.ShouldBe(isTransient);
    }

    [Fact]
    public async Task ATokenRequestThatNeedsAUser_IsDefinitive()
    {
        _respond = (_, _, _, _) => throw new AuthenticationFailedException("The credential failed.", new MsalUiRequiredException("invalid_grant", "A user must sign in."));

        (await Resolve("kv://my-vault/db-password")).IsTransient.ShouldBeFalse();
    }

    [Fact]
    public async Task AManagedIdentityEndpointThatDidNotAnswer_IsDefinitive()
    {
        // The chain Azure.Identity throws once its retries give up; it throws the same on a machine without a managed
        // identity, so it cannot tell an outage apart.
        var attempts = new AggregateException(new RequestFailedException("Connection refused."), new RequestFailedException("Connection refused."));
        var cause = new MsalServiceException("managed_identity_unreachable_network", "The managed identity endpoint did not answer.", attempts);
        _respond = (_, _, _, _) => throw new CredentialUnavailableException("Multiple attempts failed to obtain a token from the managed identity endpoint.", cause);

        var exception = await Resolve("kv://my-vault/db-password");

        exception.IsTransient.ShouldBeFalse();
        exception.InnerException.ShouldBeOfType<CredentialUnavailableException>();
        exception.Message.ShouldContain(nameof(CredentialUnavailableException));
    }

    [Theory]
    [InlineData("https://evil.com＠myvault.vault.azure.net/secrets/x")]
    [InlineData("https://docs.example.com／.vault.azure.net/secrets/readme")]
    [InlineData("https://evil․com.vault.azure.net/secrets/x")]
    [InlineData("https://myvault.vault.azure.net。/secrets/x")]
    public async Task AUnicodeHostWithoutADnsAsciiForm_IsDeclined_SoTheNextResolverGetsIt(string value)
    {
        var resolver = SecretReferenceResolver.FirstOf(Create(), new FallbackResolver(value));

        resolver.CanResolve(value).ShouldBeTrue();
        (await resolver.ResolveAsync(value, CancellationToken.None)).ShouldBe("from the fallback");
        _clients.ShouldBeEmpty();
    }

    [Fact]
    public async Task Errors_NeverHoldTheValue_NorTheTextOfTheAnswer()
    {
        // The value was read before; a read of another version fails. The message is the resolver's own: the answer's
        // text stays in the inner exception.
        _respond = (_, name, version, _) => version is null
            ? Task.FromResult(StubSecretClient.Secret(name, version, SecretValue))
            : throw new RequestFailedException((int)HttpStatusCode.NotFound, "Service request failed. Content: {\"error\":{\"code\":\"SecretNotFound\"}}", "SecretNotFound", null);
        var resolver = Create();

        (await resolver.ResolveAsync("kv://my-vault/db-password", CancellationToken.None)).ShouldBe(SecretValue);
        var exception = await Resolve($"kv://my-vault/db-password/{Version}", resolver);

        exception.Message.ShouldBe($"The vault my-vault.vault.azure.net does not hold version {Version} of the secret db-password (404 SecretNotFound).");
        exception.ToString().ShouldNotContain(SecretValue);
    }

    [Fact]
    public async Task ASecretWithoutAValue_IsDefinitive()
    {
        _respond = (_, name, version, _) => Task.FromResult(StubSecretClient.Secret(name, version, null));

        var exception = await Resolve("kv://my-vault/db-password");

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("no value");
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("not a host/", null, null)]
    [InlineData(null, "a b", null)]
    [InlineData(null, "vault.azure.cn.", null)]
    [InlineData(null, null, " ")]
    public void InvalidOptions_Throw(string? defaultSuffix, string? hostSuffix, string? vault)
    {
        var options = new KeyVaultReferenceResolverOptions();

        if (defaultSuffix is not null)
        {
            options.DefaultVaultSuffix = defaultSuffix;
        }

        if (hostSuffix is not null)
        {
            options.AllowedHostSuffixes.Add(hostSuffix);
        }

        if (vault is not null)
        {
            options.AllowedVaults.Add(vault);
        }

        Should.Throw<ArgumentException>(() => new KeyVaultReferenceResolver(new FailingCredential(), options)).ParamName.ShouldBe("options");
    }

    [Fact]
    public void ChangesToTheOptions_AfterTheResolverIsCreated_ChangeNothing()
    {
        var options = new KeyVaultReferenceResolverOptions();
        var resolver = new KeyVaultReferenceResolver(new FailingCredential(), options);

        options.AllowedVaults.Add("other");
        options.AllowedHostSuffixes.Add("vault.azure.cn");

        resolver.CanResolve("kv://my-vault/x").ShouldBeTrue();
        resolver.CanResolve("https://my-vault.vault.azure.cn/secrets/x").ShouldBeFalse();
    }

    [Fact]
    public async Task WithFirstOf_AnotherResolverTakesWhatKeyVaultDeclines()
    {
        var keyVault = Create(options => options.AllowedVaults.Add("my-vault"));
        var resolver = SecretReferenceResolver.FirstOf(keyVault, new FallbackResolver());

        (await resolver.ResolveAsync("kv://my-vault/db-password", CancellationToken.None)).ShouldBe(SecretValue);
        (await resolver.ResolveAsync("kv://other-vault/db-password", CancellationToken.None)).ShouldBe("from the fallback");
    }

    private static KeyVaultReferenceResolver CreateWithTransport(HttpMessageHandler handler, TokenCredential? credential = null)
    {
        var clientOptions = new SecretClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
            Retry = { Mode = RetryMode.Fixed, Delay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(1) },
        };

        return new KeyVaultReferenceResolver(credential ?? new FailingCredential(), new KeyVaultReferenceResolverOptions { SecretClientOptions = clientOptions });
    }

    // Key Vault's answer to a read without a token: the tenant whose token it wants.
    private static HttpResponseMessage KeyVaultChallenge()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Bearer authorization=\"https://login.microsoftonline.com/{TenantId}\", resource=\"https://vault.azure.net\"");
        return response;
    }

    private static HttpResponseMessage EntraError(HttpStatusCode status, string error)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent($$"""{"error":"{{error}}","error_description":"AADSTS00000: The token request failed."}""", Encoding.UTF8, "application/json"),
        };
    }

    private async Task<SecretResolutionException> Resolve(string value, KeyVaultReferenceResolver? resolver = null)
    {
        resolver ??= Create();
        return await Should.ThrowAsync<SecretResolutionException>(() => resolver.ResolveAsync(value, CancellationToken.None).AsTask());
    }

    private KeyVaultReferenceResolver Create(Action<KeyVaultReferenceResolverOptions>? configure = null)
    {
        var options = new KeyVaultReferenceResolverOptions
        {
            SecretClientFactory = vaultUri =>
            {
                var client = new StubSecretClient(vaultUri);
                client.Respond = (name, version, cancellationToken) => _respond(vaultUri, name, version, cancellationToken);
                _clients.Add(client);
                return client;
            },
        };

        configure?.Invoke(options);
        return new KeyVaultReferenceResolver(new FailingCredential(), options);
    }

    // The tests never reach a token endpoint.
    private sealed class FailingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("No token in tests.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("No token in tests.");
        }
    }

    private sealed class FallbackResolver : ISecretReferenceResolver
    {
        private readonly string? _accepted;

        public FallbackResolver(string? accepted = null)
        {
            _accepted = accepted;
        }

        public bool CanResolve(string value)
        {
            return _accepted is null ? value.StartsWith("kv://", StringComparison.Ordinal) : value == _accepted;
        }

        public ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult("from the fallback");
        }
    }

    // The network of a real client: answers, or throws, per request; the first request is 1.
    private sealed class FailingHandler : HttpMessageHandler
    {
        private readonly Func<int, CancellationToken, HttpResponseMessage> _respond;
        private int _attempts;

        public FailingHandler(Func<int, CancellationToken, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_respond(Interlocked.Increment(ref _attempts), cancellationToken));
        }
    }
}
