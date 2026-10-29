using System.Net;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class ClientCredentialsTests
{
    private const string Secret = "cl13nt-s3cret";
    private const string TokenUrl = "https://idp.example.com/oauth2/token";

    private static readonly DateTimeOffset Start = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly StubHttpHandler _http = new();

    [Fact]
    public async Task TheTokenRequest_IsAFormPostWithoutAuthorization()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));
        var credential = Create(new ClientCredentialsOptions { TokenUrl = new Uri(TokenUrl), ClientId = "machine-client", ClientSecret = Secret, Scope = "Configuration.Read" });

        var authorization = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "token-1"));
        credential.CanRenew.ShouldBeTrue();
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri(TokenUrl));
        request.Authorization.ShouldBeNull();
        request.ContentType.ShouldBe("application/x-www-form-urlencoded");
        request.Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "machine-client",
            ["client_secret"] = Secret,
            ["scope"] = "Configuration.Read",
        });
    }

    // Disposing a copy closes its own token client; a handler of the caller, and the other copies, keep working.
    [Fact]
    public async Task Disposing_ACopy_LeavesTheCallersHandlerAndTheOtherCopies()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-2"));
        var first = Create();
        var second = Create();

        await first.GetAuthorizationAsync(null, CancellationToken.None);
        first.Dispose();
        first.Dispose();

        (await second.GetAuthorizationAsync(null, CancellationToken.None)).ShouldBe(new AuthenticationHeaderValue("Bearer", "token-2"));
    }

    [Fact]
    public async Task WithoutAScope_NoScopeIsSent()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));

        await Create().GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests.ShouldHaveSingleItem().Form.ContainsKey("scope").ShouldBeFalse();
    }

    [Fact]
    public async Task TheToken_IsCached()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));
        var credential = Create();

        await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(30));
        var second = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        second!.Parameter.ShouldBe("token-1");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Respond(async (_, _) =>
        {
            await release.Task;
            return StubHttpHandler.Response(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));
        });
        var credential = Create();

        var callers = Enumerable.Range(0, 10).Select(_ => credential.GetAuthorizationAsync(null, CancellationToken.None).AsTask()).ToList();
        release.SetResult();
        var results = await Task.WhenAll(callers);

        results.ShouldAllBe(result => result!.Parameter == "token-1");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareTheFailureOfOneRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Respond(async (_, _) =>
        {
            await release.Task;
            return StubHttpHandler.Response(HttpStatusCode.Unauthorized, """{ "error": "invalid_client" }""");
        });
        var credential = Create();

        var callers = Enumerable.Range(0, 5).Select(_ => credential.GetAuthorizationAsync(null, CancellationToken.None).AsTask()).ToList();
        release.SetResult();

        foreach (var caller in callers)
        {
            (await Should.ThrowAsync<StorytellerAuthenticationException>(caller)).Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
        }

        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheToken_IsRenewed60SecondsBeforeItExpires()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1", expiresIn: 600));
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-2", expiresIn: 600));
        var credential = Create();

        await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(539));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("token-1");
        _time.Advance(TimeSpan.FromSeconds(1));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("token-2");

        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AShortLivedToken_IsRenewedHalfwayThroughItsLifetime()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1", expiresIn: 60));
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-2", expiresIn: 60));
        var credential = Create();

        await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(29));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("token-1");
        _time.Advance(TimeSpan.FromSeconds(1));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("token-2");

        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneRequest_EvenWhenItsTokenIsNotFresh()
    {
        // An exp already past (a clock ahead of ours) is never fresh; the callers that waited share it all the same.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Respond(async (_, _) =>
        {
            await release.Task;
            return StubHttpHandler.Response(HttpStatusCode.OK, TestTokens.TokenResponse(TestTokens.Jwt(Start.AddSeconds(-10)), expiresIn: null));
        });
        var credential = Create();

        // Each call counts the requests before it waits at the gate, and the first holds the gate until the release.
        var callers = Enumerable.Range(0, 5).Select(_ => credential.GetAuthorizationAsync(null, CancellationToken.None).AsTask()).ToList();
        release.SetResult();
        var results = await Task.WhenAll(callers);

        results.Select(result => result!.Parameter).Distinct().ShouldHaveSingleItem();
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AFailedEarlyRenewal_KeepsTheTokenUntilItExpires()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1", expiresIn: 600));
        _http.Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        _http.Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        var credential = Create();

        await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(570));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("token-1");
        _time.Advance(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));
    }

    [Theory]
    [InlineData(3600L, 3600)]
    [InlineData(864000L, 86400)]
    public async Task TheExpiry_ComesFromExpiresIn_CappedAtADay(long expiresIn, int lifetimeSeconds)
    {
        await ShouldRenewAfter(TestTokens.TokenResponse("token-1", expiresIn), TimeSpan.FromSeconds(lifetimeSeconds));
    }

    [Fact]
    public async Task TheExpiry_ComesFromTheJwtExp_WithoutExpiresIn()
    {
        var jwt = TestTokens.Jwt(Start.AddMinutes(20));

        await ShouldRenewAfter(TestTokens.TokenResponse(jwt, expiresIn: null), TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task TheExpiry_FallsBackToFiveMinutes()
    {
        await ShouldRenewAfter(TestTokens.TokenResponse("opaque-token", expiresIn: null, tokenType: null), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ExpiresIn_AsAString_IsRead()
    {
        await ShouldRenewAfter("""{ "access_token": "token-1", "expires_in": "1200" }""", TimeSpan.FromSeconds(1200));
    }

    [Fact]
    public async Task TenConcurrent401s_MakeExactlyOneTokenRequest()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Respond(async (_, _) =>
        {
            await release.Task;
            return StubHttpHandler.Response(HttpStatusCode.OK, TestTokens.TokenResponse("token-2"));
        });
        var credential = Create();
        var rejected = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        var renewals = Enumerable.Range(0, 10).Select(_ => Task.Run(() => credential.GetAuthorizationAsync(rejected, CancellationToken.None).AsTask())).ToList();
        release.SetResult();
        var results = await Task.WhenAll(renewals);

        results.ShouldAllBe(result => result!.Parameter == "token-2");
        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnOlderRejectedValue_DoesNotDropANewerToken()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));
        var credential = Create();
        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        var result = await credential.GetAuthorizationAsync(new AuthenticationHeaderValue("Bearer", "token-0"), CancellationToken.None);

        result!.Parameter.ShouldBe("token-1");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidClient_IsRejected_WithTheOAuthErrorButNoSecret()
    {
        _http.Respond(HttpStatusCode.Unauthorized, """{ "error": "invalid_client", "error_description": "Client authentication failed." }""");

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await Create().GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
        exception.Message.ShouldContain("invalid_client: Client authentication failed");
        exception.Message.ShouldNotContain(Secret);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, StorytellerAuthenticationFailureReason.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, StorytellerAuthenticationFailureReason.Rejected)]
    [InlineData(HttpStatusCode.NotFound, StorytellerAuthenticationFailureReason.Rejected)]
    [InlineData(HttpStatusCode.Found, StorytellerAuthenticationFailureReason.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, StorytellerAuthenticationFailureReason.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, StorytellerAuthenticationFailureReason.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, StorytellerAuthenticationFailureReason.Unavailable)]
    public async Task Statuses_MapToReasons(HttpStatusCode status, StorytellerAuthenticationFailureReason reason)
    {
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(status, "{}"));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(
            async () => await Create(retries: 0).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(reason);
    }

    [Fact]
    public async Task ANetworkError_IsUnavailable()
    {
        _http.Otherwise = (_, _) => throw new HttpRequestException("Connection refused.");

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(
            async () => await Create(retries: 0).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Unavailable);
        exception.IsTransient.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{ "token_type": "Bearer", "expires_in": 3600 }""")]
    [InlineData("""{ "access_token": "token-1", "token_type": "mac" }""")]
    [InlineData("<html>sign in</html>")]
    public async Task AnAnswerWithoutABearerToken_IsRejected(string body)
    {
        _http.Respond(HttpStatusCode.OK, body);

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await Create().GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
    }

    [Fact]
    public async Task TheCallersCancellation_ReachesTheRequest()
    {
        _http.Respond(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Not cancelled.");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(async () => await Create().GetAuthorizationAsync(null, cancellation.Token));
    }

    [Fact]
    public void AClientIdOutsideAuthKit_NeedsATokenUrl()
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(
            () => StorytellerCredential.FromClientCredentials(new ClientCredentialsOptions { ClientId = "keycloak-machine", ClientSecret = Secret }));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("ClientCredentialsOptions.TokenUrl");
    }

    [Fact]
    public async Task AuthKitDiscovery_FindsTheTokenUrl_Once()
    {
        _http.Respond(HttpStatusCode.OK, """{ "Provider": "AuthKit", "ClientId": "client_users", "AuthKitDomain": "https://auth.example.com" }""");
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-1"));
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("token-2"));
        var credential = CreateAuthKit();

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        await credential.GetAuthorizationAsync(first, CancellationToken.None);

        var requests = _http.Requests;
        requests.Count.ShouldBe(3);
        requests[0].Method.ShouldBe(HttpMethod.Get);
        requests[0].Uri.ShouldBe(new Uri("https://api.example.com/api/v1/auth/configuration"));
        requests[0].Authorization.ShouldBeNull();
        requests[1].Uri.ShouldBe(new Uri("https://auth.example.com/oauth2/token"));
        requests[1].Form["client_id"].ShouldBe("client_01MACHINE");
        requests[2].Uri.ShouldBe(new Uri("https://auth.example.com/oauth2/token"));
    }

    [Theory]
    [InlineData("""{ "Provider": "EntraId", "ClientId": "x", "TenantId": "t" }""")]
    [InlineData("""{ "Provider": "AuthKit", "AuthKitDomain": "https://auth.example.com/tenant" }""")]
    [InlineData("""{ "Provider": "AuthKit", "AuthKitDomain": "http://auth.example.com" }""")]
    [InlineData("""{ "Provider": "AuthKit", "AuthKitDomain": "https://user@auth.example.com" }""")]
    [InlineData("""{ "Provider": "AuthKit" }""")]
    public async Task AuthKitDiscovery_RefusesAnotherProviderOrAnUnsafeDomain(string discovery)
    {
        _http.Respond(HttpStatusCode.OK, discovery);

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await CreateAuthKit().GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
        _http.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    [Fact]
    public async Task AuthKitDiscovery_WithoutAnEndpoint_IsAConfigurationError()
    {
        var credential = ClientCredentialsCredential
            .Create(new ClientCredentialsOptions { ClientId = "client_01MACHINE", ClientSecret = Secret }, _ => null)
            .WithPrimaryHandler(_http);

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://idp.example.com/token")]
    [InlineData("https://user:password@idp.example.com/token")]
    public void ATokenUrlThatBreaksTheLocalUrlRule_IsAConfigurationError(string tokenUrl)
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(() => StorytellerCredential.FromClientCredentials(
            new ClientCredentialsOptions { TokenUrl = new Uri(tokenUrl), ClientId = "machine-client", ClientSecret = Secret }));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldNotContain("password");
    }

    [Fact]
    public void TheOptionsAndTheCredential_PrintNoSecret()
    {
        var options = new ClientCredentialsOptions { TokenUrl = new Uri(TokenUrl), ClientId = "machine-client", ClientSecret = Secret };

        options.ToString().ShouldNotContain(Secret);
        options.ToString().ShouldContain("machine-client");
        StorytellerCredential.FromClientCredentials(options).ToString()!.ShouldNotContain(Secret);
    }

    private async Task ShouldRenewAfter(string tokenResponse, TimeSpan lifetime)
    {
        _http.Respond(HttpStatusCode.OK, tokenResponse);
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("renewed"));
        var credential = Create();

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _time.Advance(lifetime - TimeSpan.FromSeconds(61));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None)).ShouldBe(first);
        _time.Advance(TimeSpan.FromSeconds(1));
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("renewed");
    }

    private StorytellerCredential Create(ClientCredentialsOptions? options = null, int retries = 3)
    {
        options ??= new ClientCredentialsOptions { TokenUrl = new Uri(TokenUrl), ClientId = "machine-client", ClientSecret = Secret };

        return StorytellerCredential.FromClientCredentials(options)
            .Bind(new StorytellerCredentialBinding(new Uri("https://api.example.com/api"))
            {
                TimeProvider = _time,
                Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero, MaxRetryAttempts = retries },
            })
            .WithPrimaryHandler(_http);
    }

    private StorytellerCredential CreateAuthKit()
    {
        return Create(new ClientCredentialsOptions { ClientId = "client_01MACHINE", ClientSecret = Secret });
    }
}
