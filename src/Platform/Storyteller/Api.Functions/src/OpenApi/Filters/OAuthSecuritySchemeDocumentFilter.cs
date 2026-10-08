using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Removes the <c>integrated</c> OAuth2 scheme when <see cref="OAuthFlows"/> has no real flow
/// (AuthKit without an AuthKit domain), so the document lists only the bearer scheme. In that case
/// <see cref="OAuthFlows"/> carries a marked stand-in flow, because the extension cannot build an
/// OAuth2 requirement without one.
/// </summary>
public class OAuthSecuritySchemeDocumentFilter : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        var schemes = document.Components?.SecuritySchemes;

        if (schemes is null
            || !schemes.TryGetValue(Definitions.SecuritySchemas.Integrated, out var scheme)
            || HasRealFlows(scheme.Flows))
        {
            return;
        }

        schemes.Remove(Definitions.SecuritySchemas.Integrated);

        foreach (var operation in document.Paths?.Values.SelectMany(path => path.Operations.Values) ?? [])
        {
            if (operation.Security is null)
            {
                continue;
            }

            foreach (var requirement in operation.Security)
            {
                foreach (var key in requirement.Keys.Where(IsIntegrated).ToList())
                {
                    requirement.Remove(key);
                }
            }

            operation.Security = operation.Security.Where(requirement => requirement.Count > 0).ToList();
        }
    }

    private static bool HasRealFlows(OpenApiOAuthFlows? flows)
    {
        return flows is not null
            && !OAuthFlows.IsPlaceholder(flows)
            && (flows.Implicit is not null
                || flows.Password is not null
                || flows.ClientCredentials is not null
                || flows.AuthorizationCode is not null);
    }

    private static bool IsIntegrated(OpenApiSecurityScheme scheme)
    {
        return scheme.Reference?.Id == Definitions.SecuritySchemas.Integrated;
    }
}
