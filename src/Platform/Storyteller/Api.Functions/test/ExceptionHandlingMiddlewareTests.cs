using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text.Json;

using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Binding.Object;
using _42.Platform.Storyteller.Configuring;

using Azure.Core.Serialization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ExceptionHandlingMiddlewareTests
{
    private const string Secret = "SECRET_SENTINEL";

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

    // A queue or timer invocation has no response to write: it fails, so the runtime delivers the message again.
    [Fact]
    public async Task Invoke_UnmappedExceptionWithoutHttpRequest_IsRethrown()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = FunctionTestDoubles.CreateNonHttp(services);

        await Should.ThrowAsync<InvalidOperationException>(() => InvokeThrowingAsync(context, new InvalidOperationException("boom")));
    }

    // The bindings below run through the real resolver and executor: the exception is the one a resolved read throws.
    [Fact]
    public async Task Invoke_SecretOperandInMath_Responds422WithoutTheSecret()
    {
        var source = new SecretSource();

        var (response, body, logger) = await ResolveThroughMiddlewareAsync("""{ "settings": { "value": "@{@value + 1}" } }""", source);

        source.SawIncludeSecrets.ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.ShouldNotContain(Secret);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe("binding.evaluation");
        json.RootElement.GetProperty(nameof(Models.ErrorResponse.Message)).GetString()
            .ShouldBe("Failed to process the data binding for 'settings.value': A math operand of type 'string' is not a number.");
        HasValue(json, nameof(Models.ErrorResponse.Error)).ShouldBeFalse();
        ShouldLogNoSecret(logger);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("Binding evaluation failed (binding.evaluation) at settings.value.");
    }

    [Fact]
    public async Task Invoke_SecretInJsonEContext_Responds422WithoutTheSecret()
    {
        var source = new SecretSource();
        var content = """
            {
              "value": {
                "$binding": "jsone",
                "$definition": { "$eval": "pin()" },
                "$context": { "pin": "@value" }
              }
            }
            """;

        var (response, body, logger) = await ResolveThroughMiddlewareAsync(content, source);

        source.SawIncludeSecrets.ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.ShouldNotContain(Secret);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe("binding.evaluation");
        HasValue(json, nameof(Models.ErrorResponse.Error)).ShouldBeFalse();
        ShouldLogNoSecret(logger);
    }

    [Fact]
    public async Task Invoke_TheIssuesFortyKilobyteBinding_Responds422ExpressionLength()
    {
        var raw = NestedCalls(10_000);

        var (response, body, logger) = await ResolveThroughMiddlewareAsync(Settings(raw), new SecretSource());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.ShouldNotContain("f(@");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe("binding.limit.expressionLength");
        HasValue(json, nameof(Models.ErrorResponse.Error)).ShouldBeFalse();
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldBe("Binding limit ExpressionLength (8192) exceeded (binding.limit.expressionLength) at settings.value.");
        entry.Values.ShouldContain(value => value.Key == "Kind" && Equals(value.Value, EvaluationLimitKind.ExpressionLength));
        entry.Values.ShouldContain(value => value.Key == "Limit" && Equals(value.Value, 8192L));
    }

    [Fact]
    public async Task Invoke_BindingDeeperThanTheLimit_Responds422Depth()
    {
        var raw = NestedCalls(65);

        var (response, body, logger) = await ResolveThroughMiddlewareAsync(Settings(raw), new SecretSource());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.ShouldNotContain("f(@");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe("binding.limit.depth");
        logger.Entries.ShouldHaveSingleItem().Message.ShouldBe("Binding limit Depth (64) exceeded (binding.limit.depth) at settings.value.");
    }

    [Fact]
    public async Task Invoke_MalformedBinding_Responds422InsteadOf500()
    {
        var (response, body, logger) = await ResolveThroughMiddlewareAsync(Settings("@a."), new SecretSource());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.ShouldNotContain("'@a.'");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty(nameof(Models.ErrorResponse.ErrorCode)).GetString().ShouldBe("binding.evaluation");
        HasValue(json, nameof(Models.ErrorResponse.Error)).ShouldBeFalse();
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    private static async Task<(TestHttpResponseData Response, string Body, CapturingLogger<ExceptionHandlingMiddleware> Logger)> ResolveThroughMiddlewareAsync(
        string content,
        IBindingSource source)
    {
        var executor = new BindingExecutor();
        executor.RegisterSource(BindingExecutor.DefaultSourceKey, source);
        var resolver = new ConfigurationBindingResolver(executor);
        var logger = new CapturingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(logger);
        var (context, bindings) = CreateContext();

        await middleware.Invoke(context, async _ => await resolver.ResolveAsync(JObject.Parse(content), includeSecrets: true, new BindingScope()));

        var response = bindings.InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, leaveOpen: true);
        return (response, await reader.ReadToEndAsync(), logger);
    }

    private static void ShouldLogNoSecret(CapturingLogger<ExceptionHandlingMiddleware> logger)
    {
        logger.Entries.ShouldNotBeEmpty();
        foreach (var entry in logger.Entries)
        {
            entry.Message.ShouldNotContain(Secret);
            foreach (var value in entry.Values)
            {
                (value.Value?.ToString() ?? string.Empty).ShouldNotContain(Secret);
            }

            entry.Exception.ShouldBeNull();
        }
    }

    private static string Settings(string raw)
    {
        return new JObject { ["settings"] = new JObject { ["value"] = raw } }.ToString();
    }

    private static string NestedCalls(int statements)
    {
        return "@" + string.Concat(Enumerable.Repeat("f(@", statements - 1)) + "x" + new string(')', statements - 1);
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
    internal sealed class HttpFunctionDefinition : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];

        public override string PathToAssembly => string.Empty;

        public override string EntryPoint => string.Empty;

        public override string Id => "function";

        public override string Name => "function";

        public override IImmutableDictionary<string, BindingMetadata> InputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;

        public override IImmutableDictionary<string, BindingMetadata> OutputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
    }

    // The default source: a secret only for a caller that may read secrets, as the key/value and Key Vault sources.
    private sealed class SecretSource : IBindingSource
    {
        public bool SawIncludeSecrets { get; private set; }

        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            SawIncludeSecrets |= request.IncludeSecrets;
            return new ValueTask<BindingValue?>(request.IncludeSecrets ? BindingValue.FromString(Secret) : null);
        }
    }
}
