using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.KeyValues;

using Azure.Core.Serialization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The client errors of key/value pairs: 403 for mTLS and the actor, 412, 424, 501 and 503 with Retry-After, and the
// items of a failed import.
public class KeyValueErrorMappingTests
{
    public static TheoryData<Exception, HttpStatusCode, string> Errors => new()
    {
        { new PreconditionFailedException("stale", ErrorCodes.KeyValueVersionMismatch), HttpStatusCode.PreconditionFailed, ErrorCodes.KeyValueVersionMismatch },
        { new FailedDependencyException("revoked", ErrorCodes.CustomerKeyUnavailable), HttpStatusCode.FailedDependency, ErrorCodes.CustomerKeyUnavailable },
        { new FeatureNotConfiguredException("no vault", ErrorCodes.ProtectionNotConfigured), HttpStatusCode.NotImplemented, ErrorCodes.ProtectionNotConfigured },
        { new ServiceUnavailableException("throttled", ErrorCodes.KeyEncryptionUnavailable, TimeSpan.FromSeconds(7)), HttpStatusCode.ServiceUnavailable, ErrorCodes.KeyEncryptionUnavailable },
        { KeyValueErrors.MtlsRequired(), HttpStatusCode.Forbidden, ErrorCodes.MtlsRequired },
        { KeyValueErrors.ActorRestricted(), HttpStatusCode.Forbidden, ErrorCodes.ActorRestricted },
        { new InvalidInputException("http", ErrorCodes.HttpsRequired), HttpStatusCode.BadRequest, ErrorCodes.HttpsRequired },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void TryMap_KnowsTheKeyValueErrors(Exception exception, HttpStatusCode expectedStatus, string expectedCode)
    {
        ErrorResponseMapping.TryMap(exception, out var status, out var response).ShouldBeTrue();

        status.ShouldBe(expectedStatus);
        response!.ErrorCode.ShouldBe(expectedCode);
        response.Error.ShouldBeNull();
    }

    [Fact]
    public void TryMap_ImportFailure_CarriesTheItems()
    {
        var items = new List<KeyValueImportItemResult>
        {
            new() { Key = "bad-key", Protection = KeyValueProtection.Public, Action = KeyValueImportAction.Unchanged, ErrorCode = ErrorCodes.InvalidKey, Message = "invalid" },
        };

        ErrorResponseMapping.TryMap(new KeyValueImportInvalidException("1 item is not valid", items), out var status, out var response).ShouldBeTrue();

        status.ShouldBe(HttpStatusCode.BadRequest);
        response!.ErrorCode.ShouldBe(ErrorCodes.ImportInvalid);
        response.Details.ShouldBeSameAs(items);
    }

    // The step-up (Auth:StepUp, RFC 9470): 401 with the code and the max age in seconds, which browsers cannot read from
    // the header (424-264).
    [Fact]
    public void TryMap_StepUpRequired_Is401WithTheMaxAge()
    {
        ErrorResponseMapping.TryMap(new StepUpRequiredException(TimeSpan.FromMinutes(10)), out var status, out var response).ShouldBeTrue();

        status.ShouldBe(HttpStatusCode.Unauthorized);
        response!.ErrorCode.ShouldBe(ErrorCodes.StepUpRequired);
        response.Message.ShouldBe("This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for 10 minutes.");
        response.Details.ShouldBe(new StepUpDetails(600));
        response.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Middleware_StepUpRequired_SendsTheChallenge()
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services);
        context.Definition = new ExceptionHandlingMiddlewareTests.HttpFunctionDefinition();
        var bindings = ExceptionHandlingMiddlewareTests.BindingsFeature.Create();
        ((TestInvocationFeatures)context.Features).Set(ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType, bindings);
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.Invoke(context, _ => throw new StepUpRequiredException(TimeSpan.FromMinutes(5)));

        var response = ((ExceptionHandlingMiddlewareTests.BindingsFeature)bindings).InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("WWW-Authenticate", out var values).ShouldBeTrue();
        values!.Single().ShouldBe("Bearer error=\"insufficient_user_authentication\", error_description=\"A recent sign-in is required\", max_age=\"300\"");
        response.Body.Position = 0;
        using var body = JsonDocument.Parse(response.Body);
        body.RootElement.GetProperty("ErrorCode").GetString().ShouldBe(ErrorCodes.StepUpRequired);
        body.RootElement.GetProperty("Details").GetProperty("MaxAge").GetInt64().ShouldBe(300);
    }

    [Fact]
    public async Task Middleware_ServiceUnavailable_SendsRetryAfter()
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services);
        context.Definition = new ExceptionHandlingMiddlewareTests.HttpFunctionDefinition();
        var bindings = ExceptionHandlingMiddlewareTests.BindingsFeature.Create();
        ((TestInvocationFeatures)context.Features).Set(ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType, bindings);
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.Invoke(context, _ => throw new ServiceUnavailableException("throttled", ErrorCodes.KeyEncryptionUnavailable, TimeSpan.FromSeconds(7)));

        var response = ((ExceptionHandlingMiddlewareTests.BindingsFeature)bindings).InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryGetValues("Retry-After", out var values).ShouldBeTrue();
        values!.Single().ShouldBe("7");
        response.Body.Position = 0;
        using var body = JsonDocument.Parse(response.Body);
        body.RootElement.GetProperty("ErrorCode").GetString().ShouldBe(ErrorCodes.KeyEncryptionUnavailable);
    }
}
