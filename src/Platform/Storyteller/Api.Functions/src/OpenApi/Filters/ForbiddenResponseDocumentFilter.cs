using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Documents <c>403 Forbidden</c> on every operation a user calls with a bearer token.
/// <para>
/// The role check of an organization or project answers 403 with an <c>ErrorResponse</c>
/// (<c>ErrorCode</c> <c>AccessDenied</c>), while 401 stays reserved for missing or invalid
/// credentials. Machine-only operations (mTLS) keep 401 and are skipped.
/// </para>
/// </summary>
public class ForbiddenResponseDocumentFilter : IDocumentFilter
{
    private const string ForbiddenStatusCode = "403";
    private const string ErrorResponseSchemaId = "ErrorResponse";

    private static readonly HashSet<string> UserSecuritySchemes =
    [
        Definitions.SecuritySchemas.Manual,
        Definitions.SecuritySchemas.Integrated,
    ];

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations.Values))
        {
            if (operation.Responses.ContainsKey(ForbiddenStatusCode) || !IsUserOperation(operation))
            {
                continue;
            }

            operation.Responses[ForbiddenStatusCode] = new OpenApiResponse
            {
                Description = Definitions.Descriptions.ResponseForbidden,
                Content =
                {
                    [Definitions.ContentTypes.Json] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = ErrorResponseSchemaId },
                        },
                    },
                },
            };
        }
    }

    private static bool IsUserOperation(OpenApiOperation operation)
    {
        return operation.Security
            .SelectMany(requirement => requirement.Keys)
            .Any(scheme => UserSecuritySchemes.Contains(scheme.Reference?.Id ?? scheme.Name ?? string.Empty));
    }
}
