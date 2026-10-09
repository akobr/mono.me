using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text.Json;

using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Configuring;

using Azure.Core.Serialization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ExceptionHandlingMiddlewareTests
{
    public static TheoryData<Exception, HttpStatusCode, string> ClientErrors => new()
    {
        { new AccessDeniedException("No Administrator access to 'acme.web'."), HttpStatusCode.Forbidden, ErrorCodes.AccessDenied },
        { new NotFoundException("The member doesn't exist.", ErrorCodes.MemberNotFound), HttpStatusCode.NotFound, ErrorCodes.MemberNotFound },
        { new ConflictException("The last owner can't leave.", ErrorCodes.LastOwner), HttpStatusCode.Conflict, ErrorCodes.LastOwner },
        { new InvalidInputException("The name is reserved.", ErrorCodes.InvalidName), HttpStatusCode.BadRequest, ErrorCodes.InvalidName },
        { new JsonPatchException("JSON Patch operation 0 failed.", JsonPatchFailureKind.TestFailed, 0), HttpStatusCode.PreconditionFailed, ErrorCodes.PatchTestFailed },
    };

    [Theory]
    [MemberData(nameof(ClientErrors))]
    public async Task Invoke_DomainException_RespondsWithStatusAndCodeWithoutDetails(Exception exception, HttpStatusCode expectedStatus, string expectedCode)
    {
        var (context, bindings) = CreateContext();

        await InvokeThrowingAsync(context, exception);

        var response = bindings.InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(expectedStatus);
        using var body = ReadBody(response);
        body.RootElement.GetProperty(nameof(Models.ErrorResponse.Message)).GetString().ShouldBe(exception.Message);
        body.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe(expectedCode);
        HasValue(body, nameof(Models.ErrorResponse.Error)).ShouldBeFalse();
    }

    [Fact]
    public async Task Invoke_UnmappedException_StillRespondsWith500()
    {
        var (context, bindings) = CreateContext();

        await InvokeThrowingAsync(context, new InvalidOperationException("boom"));

        var response = bindings.InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        using var body = ReadBody(response);
        HasValue(body, nameof(Models.ErrorResponse.Error)).ShouldBeTrue();
    }

    private static async Task InvokeThrowingAsync(FunctionContext context, Exception exception)
    {
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);

        // Thrown, not just passed, so the exception carries a stack trace that must not leave the API.
        await middleware.Invoke(context, _ => throw exception);
    }

    private static (TestFunctionContext Context, BindingsFeature Bindings) CreateContext()
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services);
        context.Definition = new HttpFunctionDefinition();
        var bindings = BindingsFeature.Create();
        ((TestInvocationFeatures)context.Features).Set(BindingsFeature.FeatureType, bindings);
        return (context, (BindingsFeature)bindings);
    }

    private static JsonDocument ReadBody(TestHttpResponseData response)
    {
        response.Body.Position = 0;
        return JsonDocument.Parse(response.Body);
    }

    private static bool HasValue(JsonDocument body, string property)
    {
        return body.RootElement.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null;
    }

    // The worker keeps the invocation result in its internal IFunctionBindingsFeature; this stands in for it.
    internal class BindingsFeature : DispatchProxy
    {
        public static readonly Type FeatureType = typeof(FunctionContext).Assembly
            .GetType("Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature", throwOnError: true)!;

        public object? InvocationResult { get; private set; }

        public static object Create()
        {
            return DispatchProxy.Create(FeatureType, typeof(BindingsFeature));
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_InvocationResult":
                    return InvocationResult;
                case "set_InvocationResult":
                    InvocationResult = args![0];
                    return null;
                case "get_OutputBindingData":
                    return new Dictionary<string, object?>();
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    // A function whose HTTP response is its return value: no named output bindings.
    private sealed class HttpFunctionDefinition : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];

        public override string PathToAssembly => string.Empty;

        public override string EntryPoint => string.Empty;

        public override string Id => "function";

        public override string Name => "function";

        public override IImmutableDictionary<string, BindingMetadata> InputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;

        public override IImmutableDictionary<string, BindingMetadata> OutputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
    }
}
