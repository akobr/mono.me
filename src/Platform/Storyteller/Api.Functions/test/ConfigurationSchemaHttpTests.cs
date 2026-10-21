using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
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
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The write of a descendant-type schema whose type cannot descend from the key: the service's InvalidInputException
// leaves the handler, and ExceptionHandlingMiddleware answers it with 400 and InvalidKey.
public class ConfigurationSchemaHttpTests
{
    private const string Org = "acme";
    private const string Project = "web";
    private const string View = "default";
    private const string CannotDescendMessage = "The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.";

    [Fact]
    public async Task SetDescendantTypeSchema_OfATypeThatCannotDescend_LeavesInvalidKeyToTheMiddleware()
    {
        var (http, schemas) = Create();

        var exception = await Should.ThrowAsync<InvalidInputException>(
            () => http.SetDescendantTypeSchema(Request(Services()), Org, Project, View, "exe.s.r.c", "rst"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        exception.Message.ShouldBe(CannotDescendMessage);
        schemas.Writes.ShouldBeEmpty();
    }

    // A schema is one Cosmos item; a larger body is refused before it is parsed or written.
    [Fact]
    public async Task SetDescendantTypeSchema_LargerThanADocument_IsTooLargeWithoutWriting()
    {
        var (http, schemas) = Create();
        var body = """{ "type": "object" }""".PadRight(RequestBodyLimits.Document + 1);

        var result = await http.SetDescendantTypeSchema(Request(Services(), body: body), Org, Project, View, "rst.payments", "exe");

        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
        error.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.BodyTooLarge);
        schemas.Writes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetDescendantTypeSchema_OfATypeThatCannotDescend_AnswersBadRequestWithInvalidKey(bool force)
    {
        var (http, schemas) = Create();
        var request = Request(Services(), force);
        var bindings = ExceptionHandlingMiddlewareTests.BindingsFeature.Create();
        ((TestInvocationFeatures)request.FunctionContext.Features).Set(ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType, bindings);
        ((TestFunctionContext)request.FunctionContext).Definition = new ExceptionHandlingMiddlewareTests.HttpFunctionDefinition();
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.Invoke(request.FunctionContext, _ => http.SetDescendantTypeSchema(request, Org, Project, View, "exe.s.r.c", "rst"));

        var response = ((ExceptionHandlingMiddlewareTests.BindingsFeature)bindings).InvocationResult.ShouldBeOfType<TestHttpResponseData>();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Body.Position = 0;
        using var body = JsonDocument.Parse(response.Body);
        body.RootElement.GetProperty(nameof(ErrorResponse.ErrorCode)).GetString().ShouldBe(ErrorCodes.InvalidKey);
        body.RootElement.GetProperty(nameof(ErrorResponse.Message)).GetString().ShouldBe(CannotDescendMessage);
        schemas.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetDescendantTypeSchema_OfATypeThatDescends_StoresTheSchema()
    {
        var (http, schemas) = Create();

        var result = await http.SetDescendantTypeSchema(Request(Services()), Org, Project, View, "rst.payments", "exe");

        var schema = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ConfigurationSchema>();
        schema.AnnotationKey.ShouldBe("rst.payments");
        schema.AnnotationType.ShouldBe(AnnotationTypeCodes.Execution);
        schemas.Writes.ShouldBe(["rst.payments exe"]);
    }

    [Fact]
    public async Task Document_DescribesInvalidKey_OnTheDescendantTypeWriteOnly()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = JsonDocument.Parse(json);

        BadRequestOf(document, Definitions.RouteIds.ConfigurationSchema.SetDescendantTypeSchema)
            .ShouldBe(Definitions.Descriptions.ResponseBadRequestDescendantType);
        BadRequestOf(document, Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchema)
            .ShouldBe(Definitions.Descriptions.ResponseBadRequest);
        BadRequestOf(document, Definitions.RouteIds.ConfigurationSchema.DeleteDescendantTypeSchema)
            .ShouldBe(Definitions.Descriptions.ResponseBadRequest);
    }

    private static string? BadRequestOf(JsonDocument document, string operationId)
    {
        return document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(entry => entry.Value)
            .Single(entry => entry.GetProperty("operationId").GetString() == operationId)
            .GetProperty("responses").GetProperty("400").GetProperty("description").GetString();
    }

    private static (ConfigurationSchemaHttp Http, DescendantSchemaProxy Schemas) Create()
    {
        var schemas = DispatchProxy.Create<IConfigurationSchemaService, DescendantSchemaProxy>();
        var http = new ConfigurationSchemaHttp(schemas, new RoleAccess(AccountRole.Contributor), NullLogger<ConfigurationSchemaHttp>.Instance);
        return (http, (DescendantSchemaProxy)(object)schemas);
    }

    private static ServiceProvider Services()
    {
        return new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .BuildServiceProvider();
    }

    private static TestHttpRequestData Request(IServiceProvider services, bool force = false, string body = """{ "type": "object" }""")
    {
        var (context, request) = FunctionTestDoubles.CreateRequest(services, method: "PUT", body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("name", "Ada"),
            new("scp", Scopes.User.Impersonation),
        };

        if (force)
        {
            request.Query[Definitions.Parameters.Force] = "true";
        }

        return request;
    }

    // The descendant-type write of the schema service: it checks the pair before it writes, as
    // CosmosConfigurationSchemaService does, and records the write. Every other member fails the test.
    public class DescendantSchemaProxy : DispatchProxy
    {
        public List<string> Writes { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IConfigurationSchemaService.SetDescendantTypeSchemaAsync))
            {
                throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
            }

            var (view, key, type) = ((string)args![2]!, (string)args[3]!, (string)args[4]!);
            DescendantTypeRules.EnsureCanDescend(AnnotationKey.Parse(key), type);
            Writes.Add($"{key} {type}");

            return Task.FromResult(new ConfigurationSchema
            {
                View = view,
                AnnotationKey = key,
                AnnotationType = type.ToLowerInvariant(),
                Version = 1,
                Content = (JObject)args[5]!,
                Modified = AuditStamp.Of((AuditActor)args[6]!, DateTimeOffset.UnixEpoch),
            });
        }
    }
}
