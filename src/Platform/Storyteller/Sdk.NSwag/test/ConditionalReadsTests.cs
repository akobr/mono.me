using System.Net;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.UnitTests;

public class ConditionalReadsTests
{
    private const string BaseUrl = "http://localhost:7071/api";
    private const string WeakTag = "W/\"e.AAECAwQFBgcICQoLDA0ODw\"";

    private const string DocumentJson = """
        {
          "AnnotationKey": "sbt.northwind",
          "Version": 3,
          "Content": { "db": { "host": "db.local", "renewal": "2026-10-20T12:00:00.1230000+02:00" } },
          "Modified": { "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } },
          "Hash": "a1b2c3d4"
        }
        """;

    private const string ErrorJson = """{ "Message": "No.", "Hint": "Ask.", "ErrorCode": "AccessDenied" }""";

    private readonly StubHttpHandler _http = new();

    public static TheoryData<bool, int, string?> Failures()
    {
        var data = new TheoryData<bool, int, string?>();

        foreach (var resolved in new[] { false, true })
        {
            data.Add(resolved, 400, ErrorJson);
            data.Add(resolved, 401, null);
            data.Add(resolved, 403, ErrorJson);
            data.Add(resolved, 404, ErrorJson);
            data.Add(resolved, 404, null);
            data.Add(resolved, 422, ErrorJson);
            data.Add(resolved, 500, ErrorJson);
            data.Add(resolved, 503, null);
            data.Add(resolved, 418, """{ "teapot": true }""");
        }

        return data;
    }

    [Theory]
    [InlineData(false, "/api/v1/acme/billing/default/configuration/sbt.northwind")]
    [InlineData(true, "/api/v1/acme/billing/default/configuration/sbt.northwind/resolved")]
    public async Task A200ThenA304_SendsTheTagBackAndReturnsNoBody(bool resolved, string path)
    {
        _http.Respond(HttpStatusCode.OK, DocumentJson, ("ETag", WeakTag), ("Cache-Control", "private, no-cache"));
        _http.Respond(HttpStatusCode.NotModified, null, ("ETag", WeakTag), ("Cache-Control", "private, no-cache"));
        var client = CreateClient();

        var first = await ReadAsync(client, resolved, "acme", "billing", "default", "sbt.northwind", null);
        var second = await ReadAsync(client, resolved, "acme", "billing", "default", "sbt.northwind", first.ETag);

        first.StatusCode.ShouldBe(200);
        first.IsNotModified.ShouldBeFalse();
        first.Result.ShouldNotBeNull().Version.ShouldBe(3);
        first.ETag.ShouldBe(WeakTag);
        second.StatusCode.ShouldBe(304);
        second.IsNotModified.ShouldBeTrue();
        second.Result.ShouldBeNull();
        second.ETag.ShouldBe(WeakTag);

        var requests = _http.Requests;
        requests.Count.ShouldBe(2);
        requests.ShouldAllBe(request => request.Uri.AbsolutePath == path && request.Method == HttpMethod.Get);
        requests[0].Headers.ContainsKey("If-None-Match").ShouldBeFalse();
        requests[1].Headers["If-None-Match"].ShouldBe(WeakTag);
    }

    [Theory]
    [InlineData(WeakTag)]
    [InlineData("W/\"r.tag\", \"other\"")]
    [InlineData("W/\"not closed")]
    [InlineData("*")]
    public async Task IfNoneMatch_IsSentUnchanged(string tag)
    {
        _http.Respond(HttpStatusCode.NotModified);

        await CreateClient().GetResolvedConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", tag);

        _http.Requests.ShouldHaveSingleItem().Headers["If-None-Match"].ShouldBe(tag);
    }

    [Fact]
    public async Task AnEmptyIfNoneMatch_IsNotSent()
    {
        _http.Respond(HttpStatusCode.OK, DocumentJson);

        await CreateClient().GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", string.Empty);

        _http.Requests.ShouldHaveSingleItem().Headers.ContainsKey("If-None-Match").ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task CacheHeaders_AreReadOnA200AndA304(HttpStatusCode status)
    {
        _http.Respond(status, status == HttpStatusCode.OK ? DocumentJson : null, ("ETag", WeakTag), ("Cache-Control", "no-store"), ("X-2S-Secrets-Withheld", "mtls-required"));
        _http.Respond(status, status == HttpStatusCode.OK ? DocumentJson : null, ("ETag", WeakTag), ("Cache-Control", "private, no-cache"));
        var client = CreateClient();

        var noStore = await client.GetResolvedConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", WeakTag);
        var noCache = await client.GetResolvedConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", WeakTag);

        noStore.CacheControl.ShouldBe("no-store");
        noStore.IsNoStore.ShouldBeTrue();
        noStore.SecretsWithheld.ShouldBe("mtls-required");

        // The raw value: a parsed Cache-Control would come back as "no-cache, private".
        noCache.CacheControl.ShouldBe("private, no-cache");
        noCache.IsNoStore.ShouldBeFalse();
        noCache.SecretsWithheld.ShouldBeNull();
        noCache.Headers["etag"].ShouldBe([WeakTag]);
        noCache.Headers["CACHE-CONTROL"].ShouldBe(["private, no-cache"]);
    }

    [Fact]
    public async Task MissingHeaders_AreNull()
    {
        _http.Respond(HttpStatusCode.OK, DocumentJson);

        var response = await CreateClient().GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        response.ETag.ShouldBeNull();
        response.CacheControl.ShouldBeNull();
        response.IsNoStore.ShouldBeFalse();
        response.SecretsWithheld.ShouldBeNull();
        response.Headers["Content-Type"].ShouldHaveSingleItem().ShouldStartWith("application/json");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheRoute_IsEscapedLikeTheGeneratedOne(bool resolved)
    {
        _http.Respond(HttpStatusCode.OK, DocumentJson);
        _http.Respond(HttpStatusCode.OK, DocumentJson);
        var client = CreateClient();
        string[] segments = ["acme corp", "bill/ing", "de fault%", "sbt.a+b?c#d&e"];

        _ = resolved
            ? await client.GetResolvedConfigurationAsync(segments[0], segments[1], segments[2], segments[3], null)
            : await client.GetConfigurationAsync(segments[0], segments[1], segments[2], segments[3], null);
        await ReadAsync(client, resolved, segments[0], segments[1], segments[2], segments[3], null);

        var requests = _http.Requests;
        requests[1].Uri.OriginalString.ShouldBe(requests[0].Uri.OriginalString);
        requests[1].Uri.AbsolutePath.ShouldContain("/v1/acme%20corp/bill%2Fing/de%20fault%25/configuration/sbt.a%2Bb%3Fc%23d%26e");
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Failures_ThrowWhatTheGeneratedMethodsThrow(bool resolved, int status, string? body)
    {
        _http.Respond((HttpStatusCode)status, body);
        _http.Respond((HttpStatusCode)status, body);
        var client = CreateClient();

        var expected = await Should.ThrowAsync<ApiException>(() => resolved
            ? client.GetResolvedConfigurationAsync("acme", "billing", "default", "sbt.northwind", null)
            : client.GetConfigurationAsync("acme", "billing", "default", "sbt.northwind", null));
        var actual = await Should.ThrowAsync<ApiException>(() => ReadAsync(client, resolved, "acme", "billing", "default", "sbt.northwind", WeakTag));

        actual.GetType().ShouldBe(expected.GetType());
        actual.StatusCode.ShouldBe(status);
        actual.Message.ShouldBe(expected.Message);
        actual.Response.ShouldBe(expected.Response);

        if (expected is ApiException<ErrorResponse> typed)
        {
            ((ApiException<ErrorResponse>)actual).Result.ErrorCode.ShouldBe(typed.Result.ErrorCode);
        }
    }

    [Theory]
    [InlineData(false, 422, false)]
    [InlineData(true, 422, true)]
    [InlineData(false, 404, true)]
    public async Task ErrorBodies_AreTypedWhereTheGeneratedMethodTypesThem(bool resolved, int status, bool typed)
    {
        _http.Respond((HttpStatusCode)status, ErrorJson);

        var exception = await Should.ThrowAsync<ApiException>(() => ReadAsync(CreateClient(), resolved, "acme", "billing", "default", "sbt.northwind", null));

        (exception is ApiException<ErrorResponse>).ShouldBe(typed);
    }

    [Fact]
    public async Task A404WithoutABody_IsAPlainApiException()
    {
        _http.Respond(HttpStatusCode.NotFound);

        var exception = await Should.ThrowAsync<ApiException>(() => CreateClient().GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.missing", null));

        exception.ShouldNotBeOfType<ApiException<ErrorResponse>>();
        exception.StatusCode.ShouldBe(404);
        exception.Message.ShouldStartWith("Response was null which was not expected.");
    }

    [Fact]
    public async Task DateLikeStrings_InTheContent_StayStrings()
    {
        _http.Respond(HttpStatusCode.OK, DocumentJson);

        var response = await CreateClient().GetResolvedConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        var renewal = response.Result!.Content.ShouldBeOfType<JObject>()["db"]!["renewal"]!;
        renewal.Type.ShouldBe(JTokenType.String);
        renewal.Value<string>().ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        response.Result.Modified.At.ShouldBe(new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Cancellation_ReachesTheSend()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Respond(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Not cancelled.");
        });
        using var cancellation = new CancellationTokenSource();

        var read = CreateClient().GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null, cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task Authorization_IsAppliedThroughTheConfiguration()
    {
        _http.Respond(HttpStatusCode.OK, DocumentJson);
        var client = new ConfigurationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl, AccessTokenFactory = () => "ApiKey 2s.key.secret" });

        await client.GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        _http.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("ApiKey 2s.key.secret");
    }

    [Fact]
    public async Task ConcurrentCalls_OnOneClient_KeepTheirOwnAnswers()
    {
        _http.Otherwise = (request, _) => Task.FromResult(request.Headers.NonValidated.TryGetValues("If-None-Match", out var tags)
            ? StubHttpHandler.Response(HttpStatusCode.NotModified, null, ("ETag", tags.ToString()))
            : StubHttpHandler.Response(HttpStatusCode.OK, DocumentJson, ("ETag", WeakTag)));
        var client = CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            client.GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", index % 2 == 0 ? null : $"W/\"e.{index}\"")));

        for (var index = 0; index < responses.Length; index++)
        {
            responses[index].IsNotModified.ShouldBe(index % 2 == 1);
            responses[index].ETag.ShouldBe(index % 2 == 0 ? WeakTag : $"W/\"e.{index}\"");
        }
    }

    private static Task<ConditionalResponse<Configuration>> ReadAsync(
        IConfigurationsApiClient client, bool resolved, string organization, string project, string view, string key, string? ifNoneMatch)
    {
        return resolved
            ? client.GetResolvedConfigurationConditionalAsync(organization, project, view, key, ifNoneMatch)
            : client.GetConfigurationConditionalAsync(organization, project, view, key, ifNoneMatch);
    }

    private ConfigurationsApiClient CreateClient()
    {
        return new ConfigurationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl });
    }
}
