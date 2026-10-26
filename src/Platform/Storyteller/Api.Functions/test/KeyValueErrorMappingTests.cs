using System.Net;
using System.Text.Json;

using _42.Platform.Storyteller.Api.ErrorHandling;
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
