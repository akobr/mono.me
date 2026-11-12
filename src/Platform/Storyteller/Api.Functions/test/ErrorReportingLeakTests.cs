using System.Text.Json;

using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.ErrorReporting;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

using Sentry;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The acceptance test of 424-248: requests and errors full of secrets go through the API's middlewares and loggers the
// way Program.cs wires them, with the real Sentry SDK; what would be sent (the serialized envelopes) holds none of them.
[Collection(SentrySdkCollection.Name)]
public class ErrorReportingLeakTests
{
    private const string FunctionCategory = "_42.Platform.Storyteller.Api.V1.KeyValuesHttp";
    private const string BackgroundCategory = "_42.Platform.Storyteller.Backend.CosmosDb.KeyValues.Usage.KeyValueUsageFlushService";
    private const string WorkerCategory = "Microsoft.Azure.Functions.Worker.FunctionsApplication";
    private const string WorkerTemplate = "Function '{functionName}', Invocation id '{invocationId}': An exception was thrown by the invocation.";
    private const string KeyValueTransaction = "PUT /api/v1/{organization}/{project}/{view}/key-values/{key}";

    private const string Jwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyXzAxIiwiZW1haWwiOiJhZGFAZXhhbXBsZS5jb20ifQ.U2lnbmF0dXJlT2ZUaGVUb2tlbg";
    private const string ApiKeySecret = "q8Zr3vT1pK0sW9yX2mN5bH7cJ4dF6gL0aE1uI3oP5sY";
    private const string ApiKey = "2s.YWNtZTpiaWxsaW5nOmNpLW1hY2hpbmU." + ApiKeySecret;
    private const string CosmosKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    private const string ConnectionString = "AccountEndpoint=https://acme.documents.azure.com:443/;AccountKey=" + CosmosKey;
    private const string Certificate = "MIIDdzCCAl+gAwIBAgIEAgAAuTANBgkqhkiG9w0BAQUFADBaMQswCQYDVQQGEwJJRTESMBAGA1UEChMJQmFsdGltb3Jl";

    // Every value that must not leave the API; each goes into another channel of an event.
    private static readonly string[] Secrets =
    [
        "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9",
        "U2lnbmF0dXJlT2ZUaGVUb2tlbg",
        ApiKeySecret,
        "C2y6yDjf5",
        "GyPMbIZnqyMsEcaGQy67XIw",
        "MIIDdzCCAl",
        "CookieSecret3101",
        "KeyValueSecret3102",
        "QuerySecret3103",
        "StructuredSecret3104",
        "ScopeDictSecret3105",
        "ScopeObjectSecret3106",
        "WarnSecret3107",
        "InnerSecret3108",
        "AggregateSecret3109",
        "DataSecret3110",
        "TagSecret3111",
        "ContextSecret3112",
        "ExtraSecret3113",
        "HelpSecret3114",
        "UserSecretId3115",
        "EnvSecret3116",
        "ada@example.com",
        "background-org",
    ];

    [Fact]
    public async Task Invoke_FailingKeyValueWrite_SendsItsErrorsWithoutAnySecret()
    {
        await using var reporting = ErrorReportingHarness.Start();

        // Outside any invocation, as the usage flush service logs: it must not ride along with the invocation's events.
        reporting.Logger(BackgroundCategory).LogWarning(
            "Could not flush the key/value usage of {Organization}.{Project}", "background-org", "background-project");

        var (context, _) = CreateKeyValueWrite(reporting, "invocation-1");
        var logger = reporting.Logger(FunctionCategory);

        await InvokeAsync(reporting, context, _ =>
        {
            // As an integration that copies the request would: the filter clears it.
            reporting.Hub.ConfigureScope(scope =>
            {
                scope.Request.Method = "PUT";
                scope.Request.Url = "https://api.2s.42for.net/api/v1/acme/billing/default/key-values/db.password?reveal=QuerySecret3103";
                scope.Request.QueryString = "reveal=QuerySecret3103";
                scope.Request.Headers["Authorization"] = "Bearer " + Jwt;
                scope.Request.Headers["X-Api-Key"] = "ApiKey " + ApiKey;
                scope.Request.Headers["X-ARR-ClientCert"] = Certificate;
                scope.Request.Headers["Cookie"] = "session=CookieSecret3101";
                scope.Request.Cookies = "session=CookieSecret3101";
                scope.Request.Data = "{\"Value\":\"KeyValueSecret3102\"}";
                scope.Request.Env["REMOTE_ADDR"] = "EnvSecret3116";
                scope.User = new SentryUser { Id = "UserSecretId3115", Email = "ada@example.com", Username = "ada" };
            });

            logger.LogWarning(
                new InvalidOperationException("token=WarnSecret3107 for ada@example.com"),
                "Retrying the write of {Key}",
                "db.password");

            using (logger.BeginScope(new Dictionary<string, object> { ["Secret"] = "ScopeDictSecret3105" }))
            using (logger.BeginScope(new ScopeState("ScopeObjectSecret3106")))
            {
                logger.LogError("Writing {Configuration} failed: {Detail}", "acme.billing", "password=StructuredSecret3104");
            }

            throw CreateLeakyException();
        });

        await reporting.FlushAsync();

        var events = reporting.Transport.Events;
        events.Count.ShouldBe(2, reporting.Transport.AllText);

        foreach (var secret in Secrets)
        {
            reporting.Transport.AllText.ShouldNotContain(secret, Case.Insensitive, $"The envelopes carry '{secret}'.");
        }

        foreach (var sentryEvent in events)
        {
            ShouldBeClean(sentryEvent, KeyValueTransaction, "SetKeyValue", "invocation-1");
            Breadcrumbs(sentryEvent).ShouldContain("Retrying the write of db.password");
        }

        events.ShouldContain(sentryEvent => Message(sentryEvent) == "Writing acme.billing failed: password=[Filtered]");
        events.Any(sentryEvent => sentryEvent.TryGetProperty("exception", out var _)).ShouldBeTrue();
    }

    [Fact]
    public async Task Invoke_ConcurrentInvocations_DoNotShareTheirScope()
    {
        await using var reporting = ErrorReportingHarness.Start();
        var logger = reporting.Logger(FunctionCategory);
        var logged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first invocation logs and waits; the second one runs whole meanwhile; then the first one fails.
        var (first, _) = CreateKeyValueWrite(reporting, "invocation-1");
        var firstRun = InvokeAsync(reporting, first, async _ =>
        {
            logger.LogWarning("The first invocation's breadcrumb");
            logged.SetResult();
            await release.Task;
            throw new InvalidOperationException("The first failure.");
        });
        await logged.Task;

        var (second, _) = FunctionTestDoubles.CreateRequest(reporting.Services, method: "GET");
        second.Definition = new NamedFunctionDefinition("GetKeyValue");
        second.Invocation = "invocation-2";
        SetBindings(second);
        await InvokeAsync(reporting, second, _ =>
        {
            logger.LogWarning("The second invocation's breadcrumb");
            throw new InvalidOperationException("The second failure.");
        });

        release.SetResult();
        await firstRun;

        // and the worker's entry after both, outside any invocation: it must not take a scope of theirs
        reporting.Logger(WorkerCategory).LogError(new IOException("The archive stream broke."), WorkerTemplate, "SetKeyValue", "invocation-w");
        await reporting.FlushAsync();

        var events = reporting.Transport.Events;
        events.Count.ShouldBe(3);
        var firstEvent = events.Single(sentryEvent => Context(sentryEvent, "invocation_id") == "invocation-1");
        var secondEvent = events.Single(sentryEvent => Context(sentryEvent, "invocation_id") == "invocation-2");
        var workerEvent = events.Single(sentryEvent => Context(sentryEvent, "invocation_id") == "invocation-w");

        ShouldBeClean(firstEvent, KeyValueTransaction, "SetKeyValue", "invocation-1");
        Breadcrumbs(firstEvent).ShouldBe(["The first invocation's breadcrumb"]);
        ShouldBeClean(secondEvent, "GET /api/v1/{organization}/{project}/{view}/key-values/{key}", "GetKeyValue", "invocation-2");
        Breadcrumbs(secondEvent).ShouldBe(["The second invocation's breadcrumb"]);
        ShouldBeClean(workerEvent, KeyValueTransaction, "SetKeyValue", "invocation-w");
        Breadcrumbs(workerEvent).ShouldBeEmpty();
        TraceId(secondEvent).ShouldNotBe(TraceId(firstEvent));
    }

    [Fact]
    public async Task Invoke_ATimeoutsCancellation_StaysAnError()
    {
        await using var reporting = ErrorReportingHarness.Start();
        using var timeout = new CancellationTokenSource();
        await timeout.CancelAsync();

        // another token than the invocation's, which runs on
        var (context, _) = CreateKeyValueWrite(reporting, "invocation-t");
        await InvokeAsync(reporting, context, _ => throw new OperationCanceledException(timeout.Token));
        await reporting.FlushAsync();

        ShouldBeClean(reporting.Transport.Events.ShouldHaveSingleItem(), KeyValueTransaction, "SetKeyValue", "invocation-t");
    }

    [Fact]
    public async Task Invoke_CancelledInvocation_SendsNoEvent()
    {
        await using var reporting = ErrorReportingHarness.Start();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var (context, _) = CreateKeyValueWrite(reporting, "invocation-1");
        context.Token = cancellation.Token;
        var exception = new OperationCanceledException(cancellation.Token);
        await InvokeAsync(reporting, context, _ => throw exception);

        // and the worker's own entry, when the exception reaches it
        reporting.Logger(WorkerCategory).LogError(exception, WorkerTemplate, "SetKeyValue", "invocation-1");
        await reporting.FlushAsync();

        reporting.Transport.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invoke_FailedQueueInvocation_IsRethrownAndSentOnce()
    {
        await using var reporting = ErrorReportingHarness.Start();
        var context = FunctionTestDoubles.CreateNonHttp(reporting.Services);
        context.Definition = new NamedFunctionDefinition("ProcessViewTransfer");
        context.Invocation = "invocation-q";
        var exception = new InvalidOperationException("The transfer failed.");

        await Should.ThrowAsync<InvalidOperationException>(() => InvokeAsync(reporting, context, _ => throw exception));

        // The worker logs the rethrown exception again, outside the middlewares: the same instance, so no second event.
        reporting.Logger(WorkerCategory).LogError(exception, WorkerTemplate, "ProcessViewTransfer", "invocation-q");
        await reporting.FlushAsync();

        var sentryEvent = reporting.Transport.Events.ShouldHaveSingleItem();
        ShouldBeClean(sentryEvent, "queue ProcessViewTransfer", "ProcessViewTransfer", "invocation-q");
    }

    [Fact]
    public async Task Log_WorkerErrorOutsideTheMiddlewares_GetsTheFunctionsTransaction()
    {
        await using var reporting = ErrorReportingHarness.Start();
        reporting.Logger(BackgroundCategory).LogWarning(
            "Could not flush the key/value usage of {Organization}.{Project}", "background-org", "background-project");
        var worker = reporting.Logger(WorkerCategory);

        // As the worker runs an invocation: its own logging scope around the middlewares, and its error entry after them,
        // e.g. of an IActionResult that fails while the proxying middleware writes it.
        using (worker.BeginScope(new List<KeyValuePair<string, object>> { new("AzureFunctions_FunctionName", "SetKeyValue"), new("AzureFunctions_InvocationId", "invocation-w") }))
        {
            var (context, _) = CreateKeyValueWrite(reporting, "invocation-w");
            await InvokeAsync(reporting, context, _ =>
            {
                reporting.Logger(FunctionCategory).LogWarning("The invocation's own breadcrumb");
                return Task.CompletedTask;
            });

            worker.LogError(new IOException("The archive stream broke."), WorkerTemplate, "SetKeyValue", "invocation-w");
        }

        await reporting.FlushAsync();

        var sentryEvent = reporting.Transport.Events.ShouldHaveSingleItem();
        ShouldBeClean(sentryEvent, KeyValueTransaction, "SetKeyValue", "invocation-w");
        Breadcrumbs(sentryEvent).ShouldBeEmpty();
        reporting.Transport.AllText.ShouldNotContain("background-org");
    }

    [Fact]
    public async Task Invoke_RepeatedError_IsSentOncePerInterval()
    {
        var time = new ManualTimeProvider();
        await using var reporting = ErrorReportingHarness.Start(new Dictionary<string, string?> { ["Sentry:RepeatInterval"] = "01:00:00" }, time);

        for (var index = 0; index < 3; index++)
        {
            var (context, _) = CreateKeyValueWrite(reporting, $"invocation-{index}");
            await InvokeAsync(reporting, context, _ => Fail());
        }

        await reporting.FlushAsync();
        reporting.Transport.Events.ShouldHaveSingleItem();

        // after the interval: sent again, with the count of the two repeats it dropped
        time.Now += TimeSpan.FromHours(1);
        var (next, _) = CreateKeyValueWrite(reporting, "invocation-3");
        await InvokeAsync(reporting, next, _ => Fail());
        await reporting.FlushAsync();

        var events = reporting.Transport.Events;
        events.Count.ShouldBe(2);
        events.Count(sentryEvent => sentryEvent.GetProperty("contexts").TryGetProperty("throttle", out var throttle)
            && throttle.GetProperty("suppressed").GetInt32() == 2).ShouldBe(1);
    }

    [Fact]
    public async Task Log_Information_LeavesBreadcrumbsOfThePlatformOnly()
    {
        await using var reporting = ErrorReportingHarness.Start();
        var (context, _) = CreateKeyValueWrite(reporting, "invocation-1");

        await InvokeAsync(reporting, context, _ =>
        {
            reporting.Logger(FunctionCategory).LogInformation("The platform's own information");
            reporting.Logger("System.Net.Http.HttpClient.Default.LogicalHandler").LogInformation("Start processing HTTP request GET https://api.workos.com/user_management/users/user_01");
            reporting.Logger(FunctionCategory).LogDebug("A debug entry");
            throw new InvalidOperationException("The write failed.");
        });

        await reporting.FlushAsync();

        var breadcrumbs = Breadcrumbs(reporting.Transport.Events.ShouldHaveSingleItem());
        breadcrumbs.ShouldContain("The platform's own information");
        breadcrumbs.ShouldNotContain(message => message.Contains("workos", StringComparison.OrdinalIgnoreCase));
        breadcrumbs.ShouldNotContain("A debug entry");
    }

    private static Task Fail()
    {
        throw new InvalidOperationException("The same failure.");
    }

    private static Exception CreateLeakyException()
    {
        var inner = new AggregateException(
            new InvalidOperationException("client_secret=InnerSecret3108"),
            new InvalidOperationException("Password=AggregateSecret3109"));
        var exception = new InvalidOperationException(
            $"Failed to write {{\"Value\":\"KeyValueSecret3102\"}} with {ConnectionString}",
            inner)
        {
            HelpLink = "https://help.example/kv?token=HelpSecret3114",
        };

        exception.Data["plain"] = "DataSecret3110";
        exception.Data["sentry:tag:leak"] = "TagSecret3111";
        exception.Data["sentry:context:leak"] = new Dictionary<string, string> { ["value"] = "ContextSecret3112" };
        exception.Data["sentry:leak"] = "ExtraSecret3113";
        return exception;
    }

    private static (TestFunctionContext Context, TestHttpRequestData Request) CreateKeyValueWrite(ErrorReportingHarness reporting, string invocationId)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + Jwt,
            ["Cookie"] = "session=CookieSecret3101",
            ["X-ARR-ClientCert"] = Certificate,
            ["Content-Type"] = "application/json",
        };
        var (context, request) = FunctionTestDoubles.CreateRequest(reporting.Services, headers, "PUT", "{\"Value\":\"KeyValueSecret3102\"}");
        context.Definition = new NamedFunctionDefinition("SetKeyValue");
        context.Invocation = invocationId;
        SetBindings(context);
        return (context, request);
    }

    private static void SetBindings(TestFunctionContext context)
    {
        ((TestInvocationFeatures)context.Features).Set(
            ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType,
            ExceptionHandlingMiddlewareTests.BindingsFeature.Create());
    }

    // The order of Program.cs: the Sentry scope first, then the exception handling, then the function.
    private static Task InvokeAsync(ErrorReportingHarness reporting, FunctionContext context, FunctionExecutionDelegate function)
    {
        var scope = new SentryScopeMiddleware(reporting.Hub);
        var exceptions = new ExceptionHandlingMiddleware(reporting.Loggers.CreateLogger<ExceptionHandlingMiddleware>());
        return scope.Invoke(context, inner => exceptions.Invoke(inner, function));
    }

    private static void ShouldBeClean(JsonElement sentryEvent, string transaction, string function, string invocationId)
    {
        sentryEvent.GetProperty("transaction").GetString().ShouldBe(transaction);
        sentryEvent.GetProperty("environment").GetString().ShouldBe("production");
        sentryEvent.GetProperty("release").GetString().ShouldBe(ErrorReportingEntryPoint.GetRelease());
        sentryEvent.TryGetProperty("server_name", out _).ShouldBeFalse();

        var tags = sentryEvent.GetProperty("tags").EnumerateObject().ToDictionary(tag => tag.Name, tag => tag.Value.GetString());
        tags.ShouldBe(new Dictionary<string, string?> { ["function"] = function });

        var functionContext = sentryEvent.GetProperty("contexts").GetProperty("function");
        functionContext.GetProperty("name").GetString().ShouldBe(function);
        functionContext.GetProperty("invocation_id").GetString().ShouldBe(invocationId);
        sentryEvent.GetProperty("contexts").TryGetProperty("leak", out _).ShouldBeFalse();

        if (sentryEvent.TryGetProperty("request", out var request))
        {
            request.EnumerateObject().ShouldBeEmpty();
        }

        if (sentryEvent.TryGetProperty("user", out var user))
        {
            user.EnumerateObject().ShouldBeEmpty();
        }

        if (sentryEvent.TryGetProperty("extra", out var extra))
        {
            extra.EnumerateObject().ShouldAllBe(property => property.Value.GetString() == SentryScrubber.Filtered);
        }
    }

    private static string? Message(JsonElement sentryEvent)
    {
        return sentryEvent.TryGetProperty("logentry", out var entry) && entry.TryGetProperty("formatted", out var formatted)
            ? formatted.GetString()
            : null;
    }

    private static List<string> Breadcrumbs(JsonElement sentryEvent)
    {
        if (!sentryEvent.TryGetProperty("breadcrumbs", out var breadcrumbs))
        {
            return [];
        }

        var values = breadcrumbs.ValueKind == JsonValueKind.Object ? breadcrumbs.GetProperty("values") : breadcrumbs;
        return values.EnumerateArray()
            .Select(breadcrumb => breadcrumb.TryGetProperty("message", out var message) ? message.GetString() ?? string.Empty : string.Empty)
            .ToList();
    }

    private static string? Context(JsonElement sentryEvent, string property)
    {
        return sentryEvent.GetProperty("contexts").GetProperty("function").GetProperty(property).GetString();
    }

    private static string? TraceId(JsonElement sentryEvent)
    {
        return sentryEvent.GetProperty("contexts").GetProperty("trace").GetProperty("trace_id").GetString();
    }

    // A logging scope that is neither text nor pairs: Sentry puts it in the extra "state".
    private sealed record ScopeState(string Value);
}
