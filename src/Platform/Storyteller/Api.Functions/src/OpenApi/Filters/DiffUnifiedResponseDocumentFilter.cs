using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

public class DiffUnifiedResponseDocumentFilter : IDocumentFilter
{
    private static readonly HashSet<string> DiffOperationIds =
    [
        "GetConfigurationVersionDiff",
        "GetConfigurationVersionDiffCustom",
        "GetConfigurationViewDiff",
    ];

    private const string UnifiedDiffExample =
        "@@ -1,4 +1,5 @@\n" +
        " {\n" +
        "-  \"owner\": \"team-a\",\n" +
        "+  \"owner\": \"team-b\",\n" +
        "+  \"tag\": \"v2\",\n" +
        "   \"version\": 1\n" +
        " }";

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        foreach (var path in document.Paths.Values)
        {
            foreach (var operation in path.Operations.Values)
            {
                if (operation.OperationId is null || !DiffOperationIds.Contains(operation.OperationId))
                {
                    continue;
                }

                if (!operation.Responses.TryGetValue("200", out var response))
                {
                    continue;
                }

                response.Content["text/plain"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema { Type = "string" },
                    Example = new OpenApiString(UnifiedDiffExample),
                };
            }
        }
    }
}
