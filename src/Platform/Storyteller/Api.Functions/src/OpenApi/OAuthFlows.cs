using _42.Platform.Storyteller.Api.Security;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

public class OAuthFlows : OpenApiOAuthSecurityFlows
{
    private const string AUTHORIZATION_URL = "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";
    private const string TOKEN_URL = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

    public OAuthFlows()
    {
        var tenantId = Environment.GetEnvironmentVariable("Auth:TenantId");
        var clientId = Environment.GetEnvironmentVariable("Auth:ClientId");

        Implicit = new OpenApiOAuthFlow
        {
            AuthorizationUrl = new Uri(string.Format(AUTHORIZATION_URL, tenantId)),
            RefreshUrl = new Uri(string.Format(TOKEN_URL, tenantId)),
            Scopes =
            {
                { $"api://{clientId}/{Scopes.Annotation.Read}", "Allows the app to read annotations" },
                { $"api://{clientId}/{Scopes.Annotation.Write}", "Allows the app to modify or create annotations" },
                { $"api://{clientId}/{Scopes.Configuration.Read}", "Allows the app to read configuration" },
                { $"api://{clientId}/{Scopes.Configuration.Write}", "Allows the app to modify or create configuration" },
                { $"api://{clientId}/{Scopes.Default.Read}", "Allows the app to read commons" },
                { $"api://{clientId}/{Scopes.Default.Write}", "Allows the app to modify or create commons" },
                { $"api://{clientId}/{Scopes.User.Impersonation}", "Allows the app to access the web API on your behalf" },
            },
        };

        ClientCredentials = new OpenApiOAuthFlow
        {
            TokenUrl = new Uri(string.Format(TOKEN_URL, tenantId)),
            Scopes =
            {
                { $"api://{clientId}/.default", "Default role(s) of the application" },
            },
        };
    }
}
