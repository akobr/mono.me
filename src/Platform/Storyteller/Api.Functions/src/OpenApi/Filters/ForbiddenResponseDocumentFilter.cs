using _42.Platform.Storyteller.Api.Security;

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
/// <para>
/// The 403 of every such operation that is not a <c>GET</c> and not in <c>ReadOnlyOperations</c>,
/// declared or added here, ends with the refusal of the restriction <c>ReadOnly</c>
/// (<c>ErrorCode</c> <c>ActorReadOnly</c>).
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
        foreach (var (method, operation) in document.Paths.Values.SelectMany(path => path.Operations))
        {
            if (!IsUserOperation(operation))
            {
                continue;
            }

            if (!operation.Responses.TryGetValue(ForbiddenStatusCode, out var forbidden))
            {
                forbidden = new OpenApiResponse
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
                operation.Responses[ForbiddenStatusCode] = forbidden;
            }

            if (IsRefusedUnderReadOnly(method, operation))
            {
                AppendReadOnly(forbidden);
            }
        }
    }

    private static bool IsUserOperation(OpenApiOperation operation)
    {
        return operation.Security
            .SelectMany(requirement => requirement.Keys)
            .Any(scheme => UserSecuritySchemes.Contains(scheme.Reference?.Id ?? scheme.Name ?? string.Empty));
    }

    // As ActorRestrictionMiddleware decides; the operation ids of ReadOnlyOperations are their function names.
    private static bool IsRefusedUnderReadOnly(OperationType method, OpenApiOperation operation)
    {
        return method is not (OperationType.Get or OperationType.Head or OperationType.Options)
            && (operation.OperationId is null || !ReadOnlyOperations.All.Contains(operation.OperationId));
    }

    // Once, also for a response object that two operations share.
    private static void AppendReadOnly(OpenApiResponse response)
    {
        var description = response.Description ?? string.Empty;

        if (description.EndsWith(Definitions.Descriptions.ForbiddenActorReadOnly, StringComparison.Ordinal))
        {
            return;
        }

        response.Description = description.Length == 0
            ? Definitions.Descriptions.ForbiddenActorReadOnly
            : $"{description} {Definitions.Descriptions.ForbiddenActorReadOnly}";
    }
}
