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

    [Theory]
    [InlineData(typeof(ConfigurationHttp), nameof(ConfigurationHttp.SetConfiguration))]
    [InlineData(typeof(ConfigurationHttp), nameof(ConfigurationHttp.PatchConfiguration))]
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

    private static OpenApiDocument Document(OpenApiOperation operation)
    {
        return new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/v1/test"] = new OpenApiPathItem
                {
                    Operations = { [OperationType.Get] = operation },
                },
            },
        };
    }
}
