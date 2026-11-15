using System.Net;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

// A federated machine whose platform token comes from code: the provider of the application is asked before every
// exchange, for any platform.
public class SubjectTokenProviderTests
{
    private const string MachineId = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
    private const string TokenEndpoint = "https://api.example.com/api/v1/acme/billing/access/token";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero));
    private readonly StubHttpHandler _http = new();
    private readonly List<StorytellerSubjectTokenRequest> _asked = [];

    [Fact]
    public async Task TheProvider_IsAskedBeforeEveryExchange_ForTheTokenEndpointAndTheMachine_AndItsTokenIsTrimmed()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-2"));
        var credential = Bind(Create((request, _) =>
        {
            _asked.Add(request);
            return ValueTask.FromResult($"  platform-token-{_asked.Count}\n");
        }));

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        (await credential.GetAuthorizationAsync(null, CancellationToken.None)).ShouldBe(first);
        var renewed = await credential.GetAuthorizationAsync(first, CancellationToken.None);

        first.ShouldBe(new AuthenticationHeaderValue("Bearer", "storyteller-1"));
        renewed!.Parameter.ShouldBe("storyteller-2");
        _asked.ShouldBe([new StorytellerSubjectTokenRequest(TokenEndpoint, MachineId), new StorytellerSubjectTokenRequest(TokenEndpoint, MachineId)]);
        _http.Requests.Select(request => request.Uri).ShouldBe([new Uri(TokenEndpoint), new Uri(TokenEndpoint)]);
        _http.Requests.Select(request => request.Form["subject_token"]).ShouldBe(["platform-token-1", "platform-token-2"]);
        _http.Requests[0].Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:access_token");
        _http.Requests[0].Form["client_id"].ShouldBe(MachineId);
    }

    // The audience is the token endpoint as the GitHub runner gets it: built from the endpoint as written.
    [Fact]
    public async Task TheAudience_IsTheTokenEndpoint_AsTheEndpointIsWritten()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var credential = Create((request, _) =>
            {
                _asked.Add(request);
                return ValueTask.FromResult("platform-token");
            })
            .Bind(new StorytellerCredentialBinding(new Uri("https://API.Example.com/api/")) { Organization = "acme corp", Project = "billing.eu", TimeProvider = _time })
            .WithPrimaryHandler(_http);

        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        _asked.ShouldHaveSingleItem().Audience.ShouldBe("https://API.Example.com/api/v1/acme%20corp/billing.eu/access/token");
    }

    [Fact]
    public async Task AStorytellerAuthenticationException_OfTheProvider_PassesAsItIs()
    {
        var failure = new StorytellerAuthenticationException(StorytellerAuthenticationFailureReason.Configuration, "The workload has no identity.");
        var credential = Bind(Create((_, _) => throw failure));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.ShouldBeSameAs(failure);
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), StorytellerAuthenticationFailureReason.Unavailable)]
    [InlineData(typeof(TaskCanceledException), StorytellerAuthenticationFailureReason.Unavailable)]
    [InlineData(typeof(TimeoutException), StorytellerAuthenticationFailureReason.Unavailable)]
    [InlineData(typeof(HttpRequestException), StorytellerAuthenticationFailureReason.Unavailable)]
    [InlineData(typeof(InvalidOperationException), StorytellerAuthenticationFailureReason.Rejected)]
    [InlineData(typeof(UnauthorizedAccessException), StorytellerAuthenticationFailureReason.Rejected)]
    public async Task AFailureOfTheProvider_IsMappedByItsType_AndNamedByItsTypeOnly(Type type, StorytellerAuthenticationFailureReason reason)
    {
        var failure = (Exception)Activator.CreateInstance(type, "The token s3cret-detail was refused.")!;
        var credential = Bind(Create((_, _) => ValueTask.FromException<string>(failure)));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(reason);
        exception.Message.ShouldBe($"The subject token provider failed with {type.Name}.");
        exception.InnerException.ShouldBeSameAs(failure);
        _http.Requests.ShouldBeEmpty();
    }

    // A provider that is no async method throws before it returns its task.
    [Fact]
    public async Task AProviderThatThrowsAtOnce_IsMappedToo()
    {
        var credential = Bind(Create((_, _) => throw new InvalidOperationException("No identity.")));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
        exception.Message.ShouldBe("The subject token provider failed with InvalidOperationException.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n")]
    public async Task AnEmptyToken_IsRejected(string? token)
    {
        var credential = Bind(Create((_, _) => ValueTask.FromResult(token!)));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
        exception.Message.ShouldBe("The subject token provider returned no token.");
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACancellationTheCallerRequested_StaysACancellation()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seen = default;
        var credential = Bind(Create(async (_, cancellationToken) =>
        {
            seen = cancellationToken;
            await cancellation.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return "platform-token";
        }));

        await Should.ThrowAsync<OperationCanceledException>(async () => await credential.GetAuthorizationAsync(null, cancellation.Token));

        seen.ShouldBe(cancellation.Token);
        _http.Requests.ShouldBeEmpty();
    }

    // A delegate has no text: two providers of one machine print the same, and share a cache identity of the provider.
    [Fact]
    public void TheCredential_PrintsItsSource_AndNothingOfTheProvider()
    {
        var first = Create((_, _) => ValueTask.FromResult("one")).ShouldBeOfType<FederatedTokenCredential>();
        var second = Create((_, _) => ValueTask.FromResult("two"));

        first.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.Provider);
        first.ToString().ShouldBe($"StorytellerCredential {{ Kind = Federated, ClientId = {MachineId}, SubjectTokenSource = Provider }}");
        second.ToString().ShouldBe(first.ToString());
        Bind(first).ToString().ShouldBe(first.ToString());
    }

    private static StorytellerCredential Create(StorytellerSubjectTokenProvider provider)
    {
        return StorytellerCredential.FromFederatedToken(new FederatedTokenOptions
        {
            ClientId = MachineId,
            SubjectTokenType = FederatedTokenOptions.AccessTokenType,
            SubjectTokenProvider = provider,
        });
    }

    private StorytellerCredential Bind(StorytellerCredential credential)
    {
        return credential
            .Bind(new StorytellerCredentialBinding(new Uri("https://api.example.com/api/"))
            {
                Organization = "acme",
                Project = "billing",
                TimeProvider = _time,
                Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero },
            })
            .WithPrimaryHandler(_http);
    }
}
