using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;
using Azure.Core.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A schema that cannot be used (InvalidSchema), that refers outside itself (ExternalSchemaReference), or whose check
// takes too long (SchemaValidationTimeout) is an InvalidInputException of the service: the schema writes leave it to
// ExceptionHandlingMiddleware, the configuration writes map it themselves, and both answer 400 with the code and the
// message, never exception details.
public class SchemaCheckFailureHttpTests
{
    private const string Org = "acme";
    private const string Project = "web";
    private const string View = "default";
    private const string Key = "rst.payments";

    public static TheoryData<string> Codes => new()
    {
        ErrorCodes.InvalidSchema,
        ErrorCodes.ExternalSchemaReference,
        ErrorCodes.SchemaValidationTimeout,
    };

    public static TheoryData<string, string> SchemaWritesAndCodes()
    {
        var data = new TheoryData<string, string>();

        foreach (var write in new[] { nameof(ConfigurationSchemaHttp.SetConfigurationSchema), nameof(ConfigurationSchemaHttp.SetAnnotationSchema), nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema) })
        {
            foreach (var code in new[] { ErrorCodes.InvalidSchema, ErrorCodes.ExternalSchemaReference, ErrorCodes.SchemaValidationTimeout })
            {
                data.Add(write, code);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SchemaWritesAndCodes))]
    public async Task SchemaWrite_LeavesTheCodedFailureToTheMiddleware(string write, string code)
    {
        var http = SchemaHttp(code);
        var request = SchemaRequest(Services());

        var exception = await Should.ThrowAsync<InvalidInputException>(() => InvokeSchemaWrite(http, write, request));

        exception.ErrorCode.ShouldBe(code);
        exception.Message.ShouldBe(MessageOf(code));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task SchemaWrite_AnswersBadRequestWithTheCode(string code)
    {
        var http = SchemaHttp(code);
        var request = SchemaRequest(Services());
        var bindings = ExceptionHandlingMiddlewareTests.BindingsFeature.Create();
        ((TestInvocationFeatures)request.FunctionContext.Features).Set(ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType, bindings);
        ((TestFunctionContext)request.FunctionContext).Definition = new ExceptionHandlingMiddlewareTests.HttpFunctionDefinition();
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.Invoke(request.FunctionContext, _ => http.SetConfigurationSchema(request, Org, Project, View, AnnotationTypeCodes.Responsibility));

        var response = ((ExceptionHandlingMiddlewareTests.BindingsFeature)bindings).InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Body.Position = 0;
        using var body = JsonDocument.Parse(response.Body);
        body.RootElement.GetProperty(nameof(ErrorResponse.ErrorCode)).GetString().ShouldBe(code);
        body.RootElement.GetProperty(nameof(ErrorResponse.Message)).GetString().ShouldBe(MessageOf(code));
        (body.RootElement.TryGetProperty(nameof(ErrorResponse.Error), out var error) && error.ValueKind != JsonValueKind.Null).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ConfigurationWrite_AnswersBadRequestWithTheCode(string code)
    {
        var http = ConfigurationHttp(code);

        var result = await http.SetConfiguration(TestRequests.User("PUT", """{ "name": "x" }"""), Org, Project, View, Key);

        ShouldBeBadRequest(result, code);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ConfigurationPatch_AnswersBadRequestWithTheCode(string code)
    {
        var http = ConfigurationHttp(code);

        var result = await http.PatchConfiguration(TestRequests.User("PATCH", """[{ "op": "add", "path": "/name", "value": "x" }]"""), Org, Project, View, Key);

        ShouldBeBadRequest(result, code);
    }

    private static string MessageOf(string code)
    {
        return $"The schema check failed with {code}.";
    }

    private static void ShouldBeBadRequest(IActionResult result, string code)
    {
        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        var response = error.Value.ShouldBeOfType<ErrorResponse>();
        response.ErrorCode.ShouldBe(code);
        response.Message.ShouldBe(MessageOf(code));
        response.Error.ShouldBeNull();
    }

    private static Task<IActionResult> InvokeSchemaWrite(ConfigurationSchemaHttp http, string write, TestHttpRequestData request)
    {
        return write switch
        {
            nameof(ConfigurationSchemaHttp.SetConfigurationSchema) => http.SetConfigurationSchema(request, Org, Project, View, AnnotationTypeCodes.Responsibility),
            nameof(ConfigurationSchemaHttp.SetAnnotationSchema) => http.SetAnnotationSchema(request, Org, Project, View, Key),
            _ => http.SetDescendantTypeSchema(request, Org, Project, View, Key, AnnotationTypeCodes.Execution),
        };
    }

    private static ConfigurationSchemaHttp SchemaHttp(string code)
    {
        return new ConfigurationSchemaHttp(Failing<IConfigurationSchemaService>(code), new RoleAccess(AccountRole.Contributor), NullLogger<ConfigurationSchemaHttp>.Instance);
    }

    private static ConfigurationHttp ConfigurationHttp(string code)
    {
        return new ConfigurationHttp(
            Failing<IConfigurationService>(code),
            DispatchProxy.Create<IAnnotationService, MachineRestrictionHttpTests.UntouchedProxy>(),
            new RoleAccess(AccountRole.Contributor),
            new FixedKeyValuePolicies(requireMtls: false),
            NullLogger<ConfigurationHttp>.Instance);
    }

    private static T Failing<T>(string code)
        where T : class
    {
        var service = DispatchProxy.Create<T, FailingProxy>();
        ((FailingProxy)(object)service).Code = code;
        return service;
    }

    private static ServiceProvider Services()
    {
        return new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .BuildServiceProvider();
    }

    private static TestHttpRequestData SchemaRequest(IServiceProvider services)
    {
        var (context, request) = FunctionTestDoubles.CreateRequest(services, method: "PUT", body: """{ "type": "object" }""");
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("name", "Ada"),
            new("scp", Scopes.User.Impersonation),
        };

        return request;
    }

    // Every call of the service fails the way the schema check does.
    public class FailingProxy : DispatchProxy
    {
        public string Code { get; set; } = ErrorCodes.InvalidSchema;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new InvalidInputException(MessageOf(Code), Code);
        }
    }
}
