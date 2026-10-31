using System.Diagnostics;
using System.Net;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Resilience;

public class StorytellerResilienceTests
{
    private readonly StubHttpHandler _api = new();

    [Fact]
    public void TheDefaults_AreThoseOfTheSpec()
    {
        var options = new StorytellerResilienceOptions();

        options.MaxRetryAttempts.ShouldBe(3);
        options.BaseDelay.ShouldBe(TimeSpan.FromMilliseconds(500));
        options.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        options.RetryUnsafeMethods.ShouldBeFalse();
        options.FailureRatio.ShouldBe(0.5);
        options.MinimumThroughput.ShouldBe(3);
        options.SamplingDuration.ShouldBe(TimeSpan.FromSeconds(60));
        options.BreakDuration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ZeroAndInfinite_TurnRetriesAndTheAttemptTimeoutOff()
    {
        Should.NotThrow(() => StorytellerResilience.CreatePipeline(new StorytellerResilienceOptions { MaxRetryAttempts = 0, AttemptTimeout = TimeSpan.Zero }));
        Should.NotThrow(() => StorytellerResilience.CreatePipeline(new StorytellerResilienceOptions { AttemptTimeout = Timeout.InfiniteTimeSpan }));
    }

    [Fact]
    public void NegativeValues_AreRejected_NotSkipped()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StorytellerResilience.CreatePipeline(new StorytellerResilienceOptions { MaxRetryAttempts = -1 }))
            .ParamName.ShouldBe("options.MaxRetryAttempts");
        Should.Throw<ArgumentOutOfRangeException>(() => StorytellerResilience.CreatePipeline(new StorytellerResilienceOptions { AttemptTimeout = TimeSpan.FromSeconds(-5) }))
            .ParamName.ShouldBe("options.AttemptTimeout");
    }

    [Fact]
    public void Clone_CopiesEveryProperty()
    {
        var options = new StorytellerResilienceOptions
        {
            MaxRetryAttempts = 1,
            BaseDelay = TimeSpan.FromSeconds(2),
            AttemptTimeout = TimeSpan.FromSeconds(3),
            RetryUnsafeMethods = true,
            FailureRatio = 0.25,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(50),
            BreakDuration = TimeSpan.FromSeconds(6),
        };

        var copy = options.Clone();

        copy.ShouldNotBeSameAs(options);
        foreach (var property in typeof(StorytellerResilienceOptions).GetProperties())
        {
            property.GetValue(copy).ShouldBe(property.GetValue(options), property.Name);
        }
    }

    [Fact]
    public async Task A503ThenA200_IsRetried()
    {
        _api.Respond(HttpStatusCode.ServiceUnavailable);
        _api.Respond(HttpStatusCode.OK, "{}");
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task APost_IsNotRetriedByTheApiPipeline()
    {
        _api.Respond(HttpStatusCode.ServiceUnavailable);
        _api.Respond(HttpStatusCode.OK, "{}");
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });

        using var response = await client.PostAsync("https://api.example.com/api/v1/acme", new StringContent("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        _api.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task APost_IsRetried_WhenUnsafeMethodsAre()
    {
        _api.Respond(HttpStatusCode.ServiceUnavailable);
        _api.Respond(HttpStatusCode.OK, "{}");
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero, RetryUnsafeMethods = true });

        using var response = await client.PostAsync("https://api.example.com/api/v1/acme", new StringContent("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Select(request => request.Body).ShouldBe(["{}", "{}"]);
    }

    [Fact]
    public async Task RetryAfter_IsHonoured()
    {
        _api.Respond(HttpStatusCode.ServiceUnavailable, null, ("Retry-After", "1"));
        _api.Respond(HttpStatusCode.OK, "{}");
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });
        var stopwatch = Stopwatch.StartNew();

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task TheBreaker_OpensAfterRepeatedFailures()
    {
        _api.Otherwise = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = CreateClient(new StorytellerResilienceOptions { MaxRetryAttempts = 0 });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.GetAsync("https://api.example.com/api/v1/acme");
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        await Should.ThrowAsync<BrokenCircuitException>(() => client.GetAsync("https://api.example.com/api/v1/acme"));
        _api.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task TheRetriesOfOneFailingCall_DoNotOpenTheBreaker_WithTheDefaults()
    {
        _api.Otherwise = (request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath.EndsWith("/broken", StringComparison.Ordinal) ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });

        using var failed = await client.GetAsync("https://api.example.com/api/v1/broken");
        using var other = await client.GetAsync("https://api.example.com/api/v1/other");

        failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Count.ShouldBe(5);
    }

    [Fact]
    public async Task TheBreaker_CountsCalls_NotAttempts()
    {
        _api.Otherwise = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });

        for (var call = 0; call < 3; call++)
        {
            using var response = await client.GetAsync("https://api.example.com/api/v1/acme");
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        await Should.ThrowAsync<BrokenCircuitException>(() => client.GetAsync("https://api.example.com/api/v1/acme"));
        _api.Requests.Count.ShouldBe(12);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotImplemented)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported)]
    public async Task ADefinitive5xx_IsNeitherRetriedNorCounted(HttpStatusCode status)
    {
        _api.Otherwise = (request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath.EndsWith("/unsupported", StringComparison.Ordinal) ? status : HttpStatusCode.OK));
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });

        for (var call = 0; call < 5; call++)
        {
            using var response = await client.GetAsync("https://api.example.com/api/v1/unsupported");
            response.StatusCode.ShouldBe(status);
        }

        using var other = await client.GetAsync("https://api.example.com/api/v1/other");

        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Count.ShouldBe(6);
    }

    [Fact]
    public async Task AnAttempt_TimesOut_AndIsRetried()
    {
        _api.Respond(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Not timed out.");
        });
        _api.Respond(HttpStatusCode.OK, "{}");
        using var client = CreateClient(new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero, AttemptTimeout = TimeSpan.FromMilliseconds(100) });

        using var response = await client.GetAsync("https://api.example.com/api/v1/acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AddStorytellerResilience_AddsThePipelineToATypedClient()
    {
        _api.Respond(HttpStatusCode.ServiceUnavailable);
        _api.Respond(HttpStatusCode.OK, """{ "AnnotationKey": "sbt.northwind", "Version": 1, "Content": {} }""");
        var services = new ServiceCollection();
        services.AddStorytellerSdk(
            () => new SdkConfiguration { BaseUrl = "https://api.example.com/api" },
            builder => builder
                .AddStorytellerResilience(options => options.BaseDelay = TimeSpan.Zero)
                .ConfigurePrimaryHttpMessageHandler(() => _api));
        await using var provider = services.BuildServiceProvider();

        var response = await provider.GetRequiredService<IConfigurationsApiClient>().GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind");

        response.StatusCode.ShouldBe(200);
        _api.Requests.Count.ShouldBe(2);
    }

    private HttpClient CreateClient(StorytellerResilienceOptions options)
    {
        return new HttpClient(new ResilienceHandler(StorytellerResilience.CreatePipeline(options)) { InnerHandler = _api });
    }
}
