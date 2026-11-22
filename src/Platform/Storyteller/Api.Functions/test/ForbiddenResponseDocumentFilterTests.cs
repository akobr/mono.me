using System.Reflection;

using _42.Platform.Storyteller.Api.OpenApi.Filters;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ForbiddenResponseDocumentFilterTests
{
    [Fact]
    public void Apply_UserOperation_AddsForbiddenWithErrorResponse()
    {
        var operation = Operation(Definitions.SecuritySchemas.Manual);
        var document = Document(operation);

        new ForbiddenResponseDocumentFilter().Apply(null!, document);

        var response = operation.Responses["403"];
        response.Description.ShouldBe(Definitions.Descriptions.ResponseForbidden);
        response.Content[Definitions.ContentTypes.Json].Schema.Reference.Id.ShouldBe("ErrorResponse");
    }

    [Fact]
    public void Apply_IntegratedOperation_AddsForbidden()
    {
        var operation = Operation(Definitions.SecuritySchemas.Integrated);

        new ForbiddenResponseDocumentFilter().Apply(null!, Document(operation));

        operation.Responses.ContainsKey("403").ShouldBeTrue();
    }

    [Theory]
    [InlineData(Definitions.SecuritySchemas.Mtls)]
    [InlineData(null)]
    public void Apply_MachineOrAnonymousOperation_LeavesResponses(string? scheme)
    {
        var operation = Operation(scheme);

        new ForbiddenResponseDocumentFilter().Apply(null!, Document(operation));

        operation.Responses.ContainsKey("403").ShouldBeFalse();
    }

    [Fact]
    public void Apply_ExistingForbidden_IsKept()
    {
        var operation = Operation(Definitions.SecuritySchemas.Manual);
        var existing = new OpenApiResponse { Description = "custom" };
        operation.Responses["403"] = existing;

        new ForbiddenResponseDocumentFilter().Apply(null!, Document(operation));

        operation.Responses["403"].ShouldBeSameAs(existing);
    }

    // Spec 424-302, 2.5: the 403 of a user operation that changes something ends with the refusal of ReadOnly, whether the
    // filter adds the 403 or the operation declares its own.
    [Theory]
    [InlineData(OperationType.Post)]
    [InlineData(OperationType.Put)]
    [InlineData(OperationType.Patch)]
    [InlineData(OperationType.Delete)]
    public void Apply_UserOperationThatChanges_NamesActorReadOnlyInIts403(OperationType method)
    {
        var added = Operation(Definitions.SecuritySchemas.Manual);
        var declared = Operation(Definitions.SecuritySchemas.Manual);
        declared.Responses["403"] = new OpenApiResponse { Description = "custom" };

        new ForbiddenResponseDocumentFilter().Apply(null!, Document(added, method));
        new ForbiddenResponseDocumentFilter().Apply(null!, Document(declared, method));

        added.Responses["403"].Description.ShouldBe($"{Definitions.Descriptions.ResponseForbidden} {Definitions.Descriptions.ForbiddenActorReadOnly}");
        declared.Responses["403"].Description.ShouldBe($"custom {Definitions.Descriptions.ForbiddenActorReadOnly}");
        Definitions.Descriptions.ForbiddenActorReadOnly.ShouldBe("Through an actor, a client app that may only read gets 403 with ErrorCode ActorReadOnly.");
    }

    // Once, also when a response object is shared or the filter runs again.
    [Fact]
    public void Apply_SharedForbidden_NamesActorReadOnlyOnce()
    {
        var shared = new OpenApiResponse { Description = "custom" };
        var first = Operation(Definitions.SecuritySchemas.Manual);
        var second = Operation(Definitions.SecuritySchemas.Manual);
        first.Responses["403"] = shared;
        second.Responses["403"] = shared;
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/v1/test"] = new OpenApiPathItem { Operations = { [OperationType.Put] = first, [OperationType.Delete] = second } },
            },
        };

        new ForbiddenResponseDocumentFilter().Apply(null!, document);
        new ForbiddenResponseDocumentFilter().Apply(null!, document);

        shared.Description.ShouldBe($"custom {Definitions.Descriptions.ForbiddenActorReadOnly}");
    }

    // A GET, and a POST that only reads (ReadOnlyOperations), pass under ReadOnly.
    [Theory]
    [InlineData(OperationType.Get, "GetAnnotation")]
    [InlineData(OperationType.Head, "GetAnnotation")]
    [InlineData(OperationType.Post, nameof(AnnotationsHttp.FindDeletedAnnotations))]
    [InlineData(OperationType.Post, nameof(KeyValuesHttp.ExportKeyValues))]
    public void Apply_UserOperationThatReads_DoesNotNameActorReadOnly(OperationType method, string operationId)
    {
        var operation = Operation(Definitions.SecuritySchemas.Manual);
        operation.OperationId = operationId;

        new ForbiddenResponseDocumentFilter().Apply(null!, Document(operation, method));

        operation.Responses["403"].Description.ShouldBe(Definitions.Descriptions.ResponseForbidden);
    }

    // No bearer token of a person reaches them: no 403 at all.
    [Theory]
    [InlineData(Definitions.SecuritySchemas.Mtls)]
    [InlineData(null)]
    public void Apply_MachineOrAnonymousOperationThatChanges_LeavesResponses(string? scheme)
    {
        var operation = Operation(scheme);

        new ForbiddenResponseDocumentFilter().Apply(null!, Document(operation, OperationType.Post));

        operation.Responses.ContainsKey("403").ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(ConfigurationHttp), nameof(ConfigurationHttp.SetConfiguration))]
    [InlineData(typeof(ConfigurationHttp), nameof(ConfigurationHttp.PatchConfiguration))]
    [InlineData(typeof(AnnotationsHttp), nameof(AnnotationsHttp.SetAnnotation))]
    [InlineData(typeof(AnnotationsHttp), nameof(AnnotationsHttp.PatchAnnotation))]
    [InlineData(typeof(TemplateHttp), nameof(TemplateHttp.SetTemplate))]
    [InlineData(typeof(TemplateHttp), nameof(TemplateHttp.PatchTemplate))]
    public void PatchCapableOperations_DocumentPreconditionFailed(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!;

        method.GetCustomAttributes<OpenApiResponseWithBodyAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ShouldContain(System.Net.HttpStatusCode.PreconditionFailed);
    }

    private static OpenApiOperation Operation(string? scheme)
    {
        var operation = new OpenApiOperation();

        if (scheme is not null)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } }] = [],
            });
        }

        return operation;
    }

    private static OpenApiDocument Document(OpenApiOperation operation, OperationType method = OperationType.Get)
    {
        return new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/v1/test"] = new OpenApiPathItem
                {
                    Operations = { [method] = operation },
                },
            },
        };
    }
}
