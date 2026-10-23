using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Configuring.Transport;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Reading;

public sealed class DocumentReaderTests : IDisposable
{
    private const string ResolvedPath = "/api/v1/acme/billing/default/configuration/sbt.northwind/resolved";
    private const string EffectivePath = "/api/v1/acme/billing/default/configuration/sbt.northwind";

    private readonly StubHttpHandler _http = new();
    private readonly RecordingLoggerFactory _logs = new();
    private readonly List<StorytellerTransport> _transports = [];

    private readonly Dictionary<string, string?> _environment = new()
    {
        ["STORYTELLER_ENDPOINT"] = TestDocuments.Endpoint,
        ["STORYTELLER_KEY"] = TestDocuments.Key,
    };

    public static TheoryData<int, string?, bool, string?> Statuses()
    {
        return new TheoryData<int, string?, bool, string?>
        {
            { 400, """{ "Message": "The key does not parse.", "ErrorCode": "BadRequest" }""", false, "BadRequest" },
            { 401, null, false, null },
            { 403, TestDocuments.ErrorJson, false, "AccessDenied" },
            { 403, """{ "Message": "Restricted.", "ErrorCode": "AnnotationRestricted" }""", false, "AnnotationRestricted" },
            { 404, null, false, null },
            { 404, """{ "Message": "Deleted.", "ErrorCode": "AnnotationDeleted" }""", false, "AnnotationDeleted" },
            { 422, """{ "Message": "Cannot evaluate.", "ErrorCode": "binding.evaluation" }""", false, "binding.evaluation" },
            { 418, null, false, null },
            { 501, null, false, null },
            { 302, null, false, null },
            { 408, null, true, null },
            { 424, """{ "Message": "The customer key is unavailable.", "ErrorCode": "CustomerKeyUnavailable" }""", true, "CustomerKeyUnavailable" },
            { 429, null, true, null },
            { 500, TestDocuments.ErrorJson, true, "AccessDenied" },
            { 502, null, true, null },
            { 503, """{ "Message": "Key encryption is unavailable.", "ErrorCode": "KeyEncryptionUnavailable" }""", true, "KeyEncryptionUnavailable" },
            { 504, null, true, null },
        };
    }

    [Fact]
    public async Task A200_IsChanged_WithItsTagAndCacheControl()
    {
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json(), ("ETag", TestDocuments.WeakTag), ("Cache-Control", "private, no-cache"));

        var outcome = await CreateReader().ReadAsync(Identity(), null, CancellationToken.None);

        var changed = outcome.ShouldBeOfType<ReadOutcome.Changed>();
        changed.ETag.ShouldBe(TestDocuments.WeakTag);
        changed.CacheControl.ShouldBe("private, no-cache");
        changed.Document.Version.ShouldBe(3);
        changed.Document.Content.ShouldBeOfType<JObject>()["db"]!["renewal"]!.Type.ShouldBe(JTokenType.String);

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe(ResolvedPath);
        request.Headers.ContainsKey("If-None-Match").ShouldBeFalse();
    }

    [Fact]
    public async Task A304_IsNotModified_AndSendsTheStoredTag()
    {
        _http.Respond(HttpStatusCode.NotModified, null, ("ETag", TestDocuments.OtherTag), ("Cache-Control", "no-store"));

        var outcome = await CreateReader().ReadAsync(Identity(), TestDocuments.WeakTag, CancellationToken.None);

        var notModified = outcome.ShouldBeOfType<ReadOutcome.NotModified>();
        notModified.ETag.ShouldBe(TestDocuments.OtherTag);
        notModified.CacheControl.ShouldBe("no-store");
        _http.Requests.ShouldHaveSingleItem().Headers["If-None-Match"].ShouldBe(TestDocuments.WeakTag);
    }

    [Fact]
    public async Task A304WithoutATag_KeepsTheStoredOne()
    {
        _http.Respond(HttpStatusCode.NotModified);

        var outcome = await CreateReader().ReadAsync(Identity(), TestDocuments.WeakTag, CancellationToken.None);

        var notModified = outcome.ShouldBeOfType<ReadOutcome.NotModified>();
        notModified.ETag.ShouldBe(TestDocuments.WeakTag);
        notModified.CacheControl.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task WithheldSecrets_FailAResolvedRead(HttpStatusCode status)
    {
        _http.Respond(status, status == HttpStatusCode.OK ? TestDocuments.Json() : null, ("ETag", TestDocuments.WeakTag), ("X-2S-Secrets-Withheld", "mtls-required"));

        var outcome = await CreateReader().ReadAsync(Identity(), TestDocuments.WeakTag, CancellationToken.None);

        var withheld = outcome.ShouldBeOfType<ReadOutcome.Withheld>();
        withheld.Reason.ShouldBe("mtls-required");
        withheld.IsTransient.ShouldBeFalse();
        withheld.Message.ShouldContain(TestDocuments.Key);
        withheld.Message.ShouldContain("STORYTELLER_CLIENT_CERTIFICATE");
        withheld.Message.ShouldContain("Secrets.Read");

        var exception = withheld.ToException(TestDocuments.Key);
        exception.StatusCode.ShouldBe((int)status);
        exception.IsTransient.ShouldBeFalse();
        exception.Key.ShouldBe(TestDocuments.Key);
    }

    [Fact]
    public async Task AnEffectiveRead_UsesItsRoute_AndIgnoresTheWithheldHeader()
    {
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json(), ("X-2S-Secrets-Withheld", "mtls-required"));

        var outcome = await CreateReader().ReadAsync(Identity(StorytellerReadMode.Effective), null, CancellationToken.None);

        outcome.ShouldBeOfType<ReadOutcome.Changed>();
        _http.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe(EffectivePath);
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Statuses_AreClassified(int status, string? body, bool isTransient, string? errorCode)
    {
        _http.Respond((HttpStatusCode)status, body);

        var outcome = await CreateReader().ReadAsync(Identity(), null, CancellationToken.None);

        var failure = outcome.ShouldBeAssignableTo<ReadOutcome.Failure>().ShouldNotBeNull();
        (failure is ReadOutcome.Transient).ShouldBe(isTransient);
        (failure is ReadOutcome.Definitive).ShouldBe(!isTransient);
        failure.IsTransient.ShouldBe(isTransient);
        failure.StatusCode.ShouldBe(status);
        failure.ErrorCode.ShouldBe(errorCode);
        failure.Message.ShouldContain(TestDocuments.Key);
        failure.Message.ShouldContain($"({status}");
        failure.Exception.ShouldBeAssignableTo<ApiException>();

        var exception = failure.ToException(TestDocuments.Key);
        exception.Key.ShouldBe(TestDocuments.Key);
        exception.StatusCode.ShouldBe(status);
        exception.ErrorCode.ShouldBe(errorCode);
        exception.IsTransient.ShouldBe(isTransient);
        exception.InnerException.ShouldBeSameAs(failure.Exception);
    }

    [Fact]
    public async Task TheMessage_NamesTheServersMessageAndHint()
    {
        _http.Respond(HttpStatusCode.Forbidden, TestDocuments.ErrorJson);

        var outcome = await CreateReader().ReadAsync(Identity(), null, CancellationToken.None);

        outcome.ShouldBeOfType<ReadOutcome.Definitive>().Message.ShouldEndWith("(403 AccessDenied): No. Ask the owner.");
    }

    [Theory]
    [InlineData(StorytellerAuthenticationFailureReason.Configuration, false)]
    [InlineData(StorytellerAuthenticationFailureReason.Rejected, false)]
    [InlineData(StorytellerAuthenticationFailureReason.Unavailable, true)]
    public async Task CredentialFailures_AreClassifiedByTheirReason(StorytellerAuthenticationFailureReason reason, bool isTransient)
    {
        var outcome = await CreateReader(options => options.Credential = TestCredential.Failing(reason))
            .ReadAsync(Identity(), null, CancellationToken.None);

        var failure = outcome.ShouldBeAssignableTo<ReadOutcome.Failure>().ShouldNotBeNull();
        failure.IsTransient.ShouldBe(isTransient);
        failure.StatusCode.ShouldBeNull();
        failure.Message.ShouldContain(reason.ToString());
        failure.Exception.ShouldBeOfType<StorytellerAuthenticationException>().Reason.ShouldBe(reason);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ANetworkError_IsTransient()
    {
        _http.Otherwise = (_, _) => throw new HttpRequestException("Connection refused.");

        var outcome = await CreateReader(options => options.Resilience.MaxRetryAttempts = 1).ReadAsync(Identity(), null, CancellationToken.None);

        outcome.ShouldBeOfType<ReadOutcome.Transient>().Exception.ShouldBeOfType<HttpRequestException>();
        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnOpenCircuit_IsTransient()
    {
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(HttpStatusCode.ServiceUnavailable));
        var reader = CreateReader();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await reader.ReadAsync(Identity(), null, CancellationToken.None)).ShouldBeOfType<ReadOutcome.Transient>();
        }

        var outcome = await reader.ReadAsync(Identity(), null, CancellationToken.None);

        var transient = outcome.ShouldBeOfType<ReadOutcome.Transient>();
        transient.Exception.ShouldBeAssignableTo<BrokenCircuitException>();
        transient.Message.ShouldContain("circuit");
        _http.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AnAttemptTimeout_IsTransient()
    {
        _http.Otherwise = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Not cancelled.");
        };

        var outcome = await CreateReader(options => options.Resilience.AttemptTimeout = TimeSpan.FromMilliseconds(50))
            .ReadAsync(Identity(), null, CancellationToken.None);

        outcome.ShouldBeOfType<ReadOutcome.Transient>().Exception.ShouldBeOfType<TimeoutRejectedException>();
    }

    [Fact]
    public async Task A200ThatDoesNotParse_IsTransient()
    {
        _http.Respond(HttpStatusCode.OK, "<html>Sign in to the Wi-Fi</html>");

        var outcome = await CreateReader().ReadAsync(Identity(), null, CancellationToken.None);

        outcome.ShouldBeOfType<ReadOutcome.Transient>().StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task AnUnknownFailure_IsTransient()
    {
        _http.Otherwise = (_, _) => throw new InvalidOperationException("Something of the application.");

        var outcome = await CreateReader().ReadAsync(Identity(), null, CancellationToken.None);

        outcome.ShouldBeOfType<ReadOutcome.Transient>().Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task TheCallersCancellation_Escapes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Otherwise = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Not cancelled.");
        };
        using var cancellation = new CancellationTokenSource();

        var read = CreateReader().ReadAsync(Identity(), null, cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task ADisposedTransport_Escapes()
    {
        var reader = CreateReader();
        _transports[0].Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(() => reader.ReadAsync(Identity(), null, CancellationToken.None));
    }

    [Fact]
    public void UnboundBindings_OfAResolvedDocument_AreWarnedWithTheirPathsButNotTheirValues()
    {
        var content = JObject.Parse("""
            {
              "db": { "password": "@kv(\"db-password\")", "host": "db.local" },
              "hosts": [ "a", "@secret(x)" ],
              "@name": "plain",
              "odd name": "@ref"
            }
            """);

        CreateReader().WarnUnboundBindings(Identity(), content);

        var entry = _logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(TestDocuments.Key);
        entry.Message.ShouldContain(" 3 ");
        entry.Message.ShouldContain("$.db.password, $.hosts[1], $['odd name']");
        entry.Message.ShouldNotContain("db-password");
        entry.Message.ShouldNotContain("secret(x)");
    }

    [Fact]
    public void UnboundBindings_AreNotWarned_InEffectiveModeOrWithoutAny()
    {
        var reader = CreateReader();

        reader.WarnUnboundBindings(Identity(StorytellerReadMode.Effective), JObject.Parse("""{ "a": "@kv(x)" }"""));
        reader.WarnUnboundBindings(Identity(), JObject.Parse("""{ "a": "b", "c": 1 }"""));
        reader.WarnUnboundBindings(Identity(), null);

        _logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void ManyUnboundBindings_AreCountedButNotAllListed()
    {
        var content = new JObject();

        for (var index = 0; index < 25; index++)
        {
            content[$"key{index}"] = $"@ref{index}";
        }

        CreateReader().WarnUnboundBindings(Identity(), content);

        var message = _logs.Entries.ShouldHaveSingleItem().Message;
        message.ShouldContain(" 25 ");
        message.ShouldContain("$.key19, …");
        message.ShouldNotContain("$.key20");
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(302, false)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(408, true)]
    [InlineData(409, false)]
    [InlineData(424, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    [InlineData(503, true)]
    [InlineData(505, true)]
    public void TransientStatuses(int status, bool isTransient)
    {
        DocumentReader.IsTransientStatus(status).ShouldBe(isTransient);
    }

    public void Dispose()
    {
        foreach (var transport in _transports)
        {
            transport.Dispose();
        }
    }

    private static DocumentIdentity Identity(StorytellerReadMode mode = StorytellerReadMode.Resolved)
    {
        return DocumentIdentity.Create(new Uri(TestDocuments.Endpoint), FullKey.Parse(TestDocuments.Key), mode);
    }

    private DocumentReader CreateReader(Action<StorytellerConfigurationOptions>? configure = null)
    {
        var options = TestOptions.Create(_environment, _http);
        options.Resilience.MaxRetryAttempts = 0;
        configure?.Invoke(options);

        var transport = TransportFactory.Create(SettingsReader.Read(options, true));
        _transports.Add(transport);
        return new DocumentReader(transport.Client, _logs.CreateLogger("test"));
    }
}
