using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.ErrorReporting;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Azure.Core.Serialization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The restriction ReadOnly (spec 424-302, 2.5): a person's token through a client app that may only read is refused
// every method but GET, HEAD and OPTIONS with 403 ActorReadOnly, except the functions of ReadOnlyOperations; every other
// caller passes unchanged.
public class ActorRestrictionMiddlewareTests
{
    private static readonly string[] ReadMethods = ["GET", "HEAD", "OPTIONS"];

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task ReadOnlyDelegation_IsRefusedAChange_WithActorReadOnly_AndALoggedWarning(string method)
    {
        var logger = new CapturingLogger<ActorRestrictionMiddleware>();
        var request = Delegated(nameof(AnnotationsHttp.SetAnnotation), method, client: TestRequests.ClientId);
        var called = false;

        var act = () => new ActorRestrictionMiddleware(logger).Invoke(request.FunctionContext, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.ActorReadOnly);
        exception.Message.ShouldBe("The token acts for the person through a client app that may only read; the change is refused.");
        called.ShouldBeFalse();

        // A refused write leaves no audit record, so the warning is its trace.
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe($"A request SetAnnotation {method} of the account {TestRequests.UserId} via {TestRequests.ActorId} (client {TestRequests.ClientId}) is refused: the client app may only read (ReadOnly)");
        var values = entry.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        values["FunctionName"].ShouldBe(nameof(AnnotationsHttp.SetAnnotation));
        values["Method"].ShouldBe(method);
        values["ActorId"].ShouldBe(TestRequests.ActorId);
        values["ClientId"].ShouldBe(TestRequests.ClientId);
    }

    // A token that names no client is logged with (none).
    [Fact]
    public async Task ReadOnlyDelegation_WithoutAClient_IsLoggedWithNoClient()
    {
        var logger = new CapturingLogger<ActorRestrictionMiddleware>();
        var request = Delegated(nameof(AnnotationsHttp.DeleteAnnotation), "DELETE");

        var act = () => new ActorRestrictionMiddleware(logger).Invoke(request.FunctionContext, _ => Task.CompletedTask);

        await act.ShouldThrowAsync<AccessDeniedException>();
        logger.Entries.ShouldHaveSingleItem().Values.ShouldContain(new KeyValuePair<string, object?>("ClientId", "(none)"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("get")]
    public async Task ReadOnlyDelegation_PassesTheMethodsThatRead(string method)
    {
        (await PassesAsync(Delegated(nameof(AnnotationsHttp.GetAnnotation), method))).ShouldBeTrue();
    }

    // The POSTs that only read: the lookup of deleted keys, and the export of the pairs.
    [Theory]
    [InlineData(nameof(AnnotationsHttp.FindDeletedAnnotations))]
    [InlineData(nameof(KeyValuesHttp.ExportKeyValues))]
    public async Task ReadOnlyDelegation_PassesTheOperationsThatOnlyRead(string functionName)
    {
        (await PassesAsync(Delegated(functionName, "POST"))).ShouldBeTrue();
    }

    // Machines, direct users, a delegation without ReadOnly, and invocations without a request pass unchanged.
    [Fact]
    public async Task OtherCallers_PassUnchanged()
    {
        var user = TestRequests.User("POST");
        var machine = TestRequests.Machine(method: "DELETE");
        var noSecretReveal = TestRequests.DelegatedUser([ActorRestrictions.NoSecretReveal], "PUT", client: TestRequests.ClientId);

        foreach (var request in new[] { user, machine, noSecretReveal })
        {
            ((TestFunctionContext)request.FunctionContext).Definition = new NamedFunctionDefinition(nameof(AnnotationsHttp.SetAnnotation));
            (await PassesAsync(request)).ShouldBeTrue(request.Method);
        }

        using var services = new ServiceCollection().BuildServiceProvider();
        var timer = FunctionTestDoubles.CreateNonHttp(services);
        timer.Definition = new NamedFunctionDefinition("KeyValueProtectionTimer");
        timer.Items[FunctionContextItemKeys.DelegatedRestrictions] = new[] { ActorRestrictions.ReadOnly };
        var called = false;

        await new ActorRestrictionMiddleware(NullLogger<ActorRestrictionMiddleware>.Instance).Invoke(timer, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.ShouldBeTrue();
    }

    // Every HTTP function that takes a method other than GET, HEAD or OPTIONS is refused under ReadOnly unless it is in
    // ReadOnlyOperations, so a new write route is refused by default; every method that reads passes.
    [Fact]
    public async Task EveryFunction_ThatTakesAChangingMethod_IsRefused_UnlessItOnlyReads()
    {
        var http = FunctionTransactionNames.All.Where(function => function.Value.Kind == "http").ToList();
        var refused = new List<string>();
        var passed = new List<string>();

        http.Count.ShouldBeGreaterThan(100);

        foreach (var (name, trigger) in http)
        {
            trigger.Methods.ShouldNotBeEmpty(name);

            foreach (var method in trigger.Methods)
            {
                var request = Delegated(name, method);
                var mayChange = !ReadMethods.Contains(method, StringComparer.Ordinal);

                if (mayChange && !ReadOnlyOperations.All.Contains(name))
                {
                    var act = () => new ActorRestrictionMiddleware(NullLogger<ActorRestrictionMiddleware>.Instance).Invoke(request.FunctionContext, _ => Task.CompletedTask);
                    (await act.ShouldThrowAsync<AccessDeniedException>(customMessage: $"{method} {name}")).ErrorCode.ShouldBe(ErrorCodes.ActorReadOnly);
                    refused.Add(name);
                }
                else
                {
                    (await PassesAsync(request)).ShouldBeTrue($"{method} {name}");
                    passed.Add(name);
                }
            }
        }

        refused.Distinct().Count().ShouldBeGreaterThan(60);
        refused.ShouldContain(nameof(AnnotationsHttp.SetAnnotation));
        refused.ShouldContain(nameof(AccessHttp.PostAccount));
        refused.ShouldContain(nameof(ViewTransferHttp.StartViewExport));
        passed.ShouldContain(nameof(AnnotationsHttp.GetAnnotation));
    }

    // The allow-list names only HTTP functions that exist and take POST alone: a POST for a body that only reads.
    [Fact]
    public void ReadOnlyOperations_AreExistingFunctionsThatTakePostAlone()
    {
        ReadOnlyOperations.All.ShouldBe([nameof(AnnotationsHttp.FindDeletedAnnotations), nameof(KeyValuesHttp.ExportKeyValues)], ignoreOrder: true);

        foreach (var name in ReadOnlyOperations.All)
        {
            FunctionTransactionNames.All.TryGetValue(name, out var trigger).ShouldBeTrue(name);
            trigger!.Kind.ShouldBe("http", name);
            trigger.Methods.ShouldBe(["POST"], name);
        }
    }

    // Inside ExceptionHandlingMiddleware, as Program.cs registers it: the refusal is a 403 with an ErrorResponse.
    [Fact]
    public async Task Refusal_IsAnswered403WithTheErrorCode()
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services, method: "PUT");
        context.Definition = new NamedFunctionDefinition(nameof(KeyValuesHttp.SetKeyValue));
        context.Items[FunctionContextItemKeys.DelegatedActor] = TestRequests.ActorId;
        context.Items[FunctionContextItemKeys.DelegatedRestrictions] = new[] { ActorRestrictions.NoSecretReveal, ActorRestrictions.ReadOnly };
        var bindings = ExceptionHandlingMiddlewareTests.BindingsFeature.Create();
        ((TestInvocationFeatures)context.Features).Set(ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType, bindings);
        var restriction = new ActorRestrictionMiddleware(NullLogger<ActorRestrictionMiddleware>.Instance);

        await new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance)
            .Invoke(context, inner => restriction.Invoke(inner, _ => throw new InvalidOperationException("the function must not run")));

        var response = ((ExceptionHandlingMiddlewareTests.BindingsFeature)bindings).InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Body.Position = 0;
        using var body = JsonDocument.Parse(response.Body);
        body.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe(ErrorCodes.ActorReadOnly);
        body.RootElement.GetProperty(nameof(Models.ErrorResponse.Message)).GetString().ShouldBe(ActorRestrictionMiddleware.ReadOnlyMessage);
    }

    [Fact]
    public void ErrorCode_IsMappedTo403()
    {
        ErrorCodes.ActorReadOnly.ShouldBe("ActorReadOnly");
        ErrorResponseMapping.TryMap(ActorRestrictionMiddleware.ReadOnlyRefused(), out var status, out var response).ShouldBeTrue();

        status.ShouldBe(HttpStatusCode.Forbidden);
        response!.ErrorCode.ShouldBe(ErrorCodes.ActorReadOnly);
        response.Message.ShouldBe(ActorRestrictionMiddleware.ReadOnlyMessage);
        response.Hint.ShouldBeNull();
        response.Details.ShouldBeNull();
    }

    private static TestHttpRequestData Delegated(string functionName, string method, string? client = null)
    {
        var request = TestRequests.DelegatedUser([ActorRestrictions.NoSecretReveal, ActorRestrictions.ReadOnly], method, client: client);
        ((TestFunctionContext)request.FunctionContext).Definition = new NamedFunctionDefinition(functionName);
        return request;
    }

    private static async Task<bool> PassesAsync(TestHttpRequestData request)
    {
        var called = false;

        await new ActorRestrictionMiddleware(NullLogger<ActorRestrictionMiddleware>.Instance).Invoke(request.FunctionContext, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        return called;
    }
}
