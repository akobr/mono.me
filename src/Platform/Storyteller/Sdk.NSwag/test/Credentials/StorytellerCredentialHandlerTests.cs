using System.Net;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class StorytellerCredentialHandlerTests
{
    private readonly StubHttpHandler _api = new();

    [Fact]
    public async Task EveryRequest_GetsTheAuthorizationOfTheCredential()
    {
        _api.Respond(HttpStatusCode.OK, "{}");
        using var client = CreateClient(new CountingCredential(canRenew: true));

        await client.GetAsync("https://api.example.com/api/v1/acme");

        _api.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("Bearer token-1");
    }

    [Fact]
    public async Task A401_IsRenewedOnce_WithTheRefusedValue()
    {
        _api.Respond(HttpStatusCode.Unauthorized);
        _api.Respond(HttpStatusCode.OK, "{}");
        var credential = new CountingCredential(canRenew: true);
        using var client = CreateClient(credential);

        using var response = await client.PostAsync("https://api.example.com/api/v1/acme", new StringContent("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Select(request => request.Authorization).ShouldBe(["Bearer token-1", "Bearer token-2"]);
        _api.Requests[1].Body.ShouldBe("{}");
        credential.Rejected.ShouldBe([null, "token-1"]);
    }

    [Fact]
    public async Task ASecond401_IsReturnedAsItIs()
    {
        _api.Respond(HttpStatusCode.Unauthorized);
        _api.Respond(HttpStatusCode.Unauthorized);
        using var client = CreateClient(new CountingCredential(canRenew: true));

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WithoutCanRenew_A401IsReturned()
    {
        _api.Respond(HttpStatusCode.Unauthorized);
        var credential = new CountingCredential(canRenew: false);
        using var client = CreateClient(credential);

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.Count.ShouldBe(1);
        credential.Rejected.ShouldBe([null]);
    }

    [Fact]
    public async Task AStreamedBody_IsNotSentAgain()
    {
        _api.Respond(HttpStatusCode.Unauthorized);
        var credential = new CountingCredential(canRenew: true);
        using var client = CreateClient(credential);

        using var response = await client.PostAsync("https://api.example.com/api/v1/acme", new StreamContent(new MemoryStream([1, 2, 3])));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.Count.ShouldBe(1);
        credential.Rejected.ShouldBe([null]);
    }

    [Fact]
    public async Task ARedirectThatChangedTheRequest_IsNotRenewed()
    {
        // As a redirect does: another URI, and no Authorization for the other host.
        _api.Respond((request, _) =>
        {
            request.RequestUri = new Uri("https://elsewhere.example.com/login");
            request.Headers.Authorization = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        var credential = new CountingCredential(canRenew: true);
        using var client = CreateClient(credential);

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.Count.ShouldBe(1);
        credential.Rejected.ShouldBe([null]);
    }

    [Fact]
    public async Task ARenewalThatReturnsTheSameValue_IsNotSentAgain()
    {
        _api.Respond(HttpStatusCode.Unauthorized);
        using var client = CreateClient(new CountingCredential(canRenew: true, renews: false));

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ACertificateCredential_LeavesAuthorizationAlone()
    {
        _api.Respond(HttpStatusCode.Unauthorized);
        using var certificate = TestCertificates.WithPrivateKey();
        using var client = CreateClient(StorytellerCredential.FromCertificate(certificate));

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.ShouldHaveSingleItem().Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task TheCallersCancellation_ReachesTheCredential()
    {
        var credential = new WaitingCredential();
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(credential);

        var request = client.GetAsync("https://api.example.com/api/v1/acme", cancellation.Token);
        await credential.Started.Task;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => request);
        _api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACredentialFailure_PropagatesAndSendsNothing()
    {
        using var client = CreateClient(StorytellerCredential.FromTokenFile("/run/missing", new System.IO.Abstractions.TestingHelpers.MockFileSystem()));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(() => client.GetAsync("https://api.example.com/api/v1/acme"));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        _api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddStorytellerCredential_AddsTheHandlerToATypedClient()
    {
        _api.Respond(HttpStatusCode.OK, """{ "AnnotationKey": "sbt.northwind", "Version": 1, "Content": {} }""");
        var services = new ServiceCollection();
        services.AddStorytellerSdk(
            () => new SdkConfiguration { BaseUrl = "https://api.example.com/api" },
            builder => builder
                .AddStorytellerCredential(StorytellerCredential.FromApiKey("2s.a2V5.secret"))
                .ConfigurePrimaryHttpMessageHandler(() => _api));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IConfigurationsApiClient>().GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind");

        _api.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("ApiKey 2s.a2V5.secret");
    }

    private HttpClient CreateClient(StorytellerCredential credential)
    {
        return new HttpClient(new StorytellerCredentialHandler(credential, _api));
    }

    // Waits for the caller's token, as a token request would.
    private sealed class WaitingCredential : StorytellerCredential
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    // token-1 first, token-2 for a refused token-1; records the rejected values it was asked with.
    private sealed class CountingCredential : StorytellerCredential
    {
        private readonly bool _canRenew;
        private readonly bool _renews;

        public CountingCredential(bool canRenew, bool renews = true)
        {
            _canRenew = canRenew;
            _renews = renews;
        }

        public List<string?> Rejected { get; } = [];

        public override bool CanRenew => _canRenew;

        public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rejected.Add(rejected?.Parameter);

            var token = rejected is null || !_renews ? "token-1" : "token-2";
            return ValueTask.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue("Bearer", token));
        }
    }
}
