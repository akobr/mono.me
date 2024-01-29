using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

public class ImplicitAuthFlow : OpenApiOAuthSecurityFlows
{
    private const string AUTHORIZATION_URL = "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";
    private const string REFRESH_URL = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

    public ImplicitAuthFlow()
    {
        // var tenantId = Environment.GetEnvironmentVariable("OpenApiAuthTenantId");
        var tenantId = "34b71aab-ff2a-4e62-a6cf-e769d89f0078";

        Implicit = new OpenApiOAuthFlow
        {
            AuthorizationUrl = new Uri(string.Format(AUTHORIZATION_URL, tenantId)),
            RefreshUrl = new Uri(string.Format(REFRESH_URL, tenantId)),
            Scopes = { { "https://graph.microsoft.com/.default", "Default scope defined in the app" } },
        };
    }
}
