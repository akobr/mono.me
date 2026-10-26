using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The OpenAPI extension creates this through the parameterless constructor, outside DI,
// so the Auth settings are read from the environment the Functions host exposes.
public class OAuthFlows : OpenApiOAuthSecurityFlows
{
    // Marks the stand-in flow used without an AuthKit domain or a Keycloak realm;
    // OAuthSecuritySchemeDocumentFilter removes it.
    public const string PlaceholderExtension = "x-storyteller-placeholder";

    private const string AUTHORIZATION_URL = "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";
    private const string TOKEN_URL = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

    public OAuthFlows()
        : this(new ConfigurationBuilder().AddEnvironmentVariables().Build())
    {
    }

    internal OAuthFlows(IConfiguration configuration)
    {
        var section = configuration.GetSection(UserAuthenticationOptions.SectionName);

        if (string.Equals(section["Provider"], nameof(IdentityProviderKind.AuthKit), StringComparison.OrdinalIgnoreCase))
        {
            ConfigureAuthKit(section.GetSection("AuthKit")["AuthKitDomain"]);
            return;
        }

        if (string.Equals(section["Provider"], nameof(IdentityProviderKind.Keycloak), StringComparison.OrdinalIgnoreCase))
        {
            ConfigureKeycloak(section.GetSection("Keycloak")["Issuer"]);
            return;
        }

        ConfigureEntraId(section["TenantId"], section["ClientId"]);
    }

    public static bool IsPlaceholder(OpenApiOAuthFlows? flows)
    {
        return flows?.ClientCredentials?.Extensions.ContainsKey(PlaceholderExtension) == true;
    }

    // AuthKit tokens for Swagger UI come from the AuthKit domain (WorkOS Connect).
    private void ConfigureAuthKit(string? authKitDomain)
    {
        if (string.IsNullOrWhiteSpace(authKitDomain))
        {
            ConfigurePlaceholder();
            return;
        }

        var domain = authKitDomain.TrimEnd('/');
        var tokenUrl = new Uri($"{domain}/oauth2/token");

        // A public client must use PKCE; OpenAPI 3.0 cannot express that, the client enables it.
        AuthorizationCode = new OpenApiOAuthFlow
        {
            AuthorizationUrl = new Uri($"{domain}/oauth2/authorize"),
            TokenUrl = tokenUrl,
            RefreshUrl = tokenUrl,
            Scopes =
            {
                { "openid", "Signs the user in" },
                { "profile", "Reads the user's name" },
                { "email", "Reads the user's email address" },
            },
        };

        ClientCredentials = new OpenApiOAuthFlow
        {
            TokenUrl = tokenUrl,
        };
    }

    // Keycloak users sign in at the realm (the authorization code flow with PKCE). Machines may come from another
    // provider, so no client credentials flow is described.
    private void ConfigureKeycloak(string? issuer)
    {
        var realm = issuer?.Trim().TrimEnd('/');

        if (!Uri.TryCreate(realm, UriKind.Absolute, out var realmUri)
            || (realmUri.Scheme != Uri.UriSchemeHttps && realmUri.Scheme != Uri.UriSchemeHttp))
        {
            ConfigurePlaceholder();
            return;
        }

        var tokenUrl = new Uri($"{realm}/protocol/openid-connect/token");

        AuthorizationCode = new OpenApiOAuthFlow
        {
            AuthorizationUrl = new Uri($"{realm}/protocol/openid-connect/auth"),
            TokenUrl = tokenUrl,
            RefreshUrl = tokenUrl,
            Scopes =
            {
                { "openid", "Signs the user in" },
                { "profile", "Reads the user's name" },
                { "email", "Reads the user's email address" },
            },
        };
    }

    // Without an AuthKit domain or a Keycloak realm there is no flow to describe. The extension still needs one,
    // because it builds each operation's requirement before document filters run and throws "Flow MUST be provided"
    // otherwise. This stand-in never reaches the published document.
    private void ConfigurePlaceholder()
    {
        ClientCredentials = new OpenApiOAuthFlow
        {
            TokenUrl = new Uri("https://placeholder.invalid/oauth2/token"),
            Extensions = { [PlaceholderExtension] = new OpenApiBoolean(true) },
        };
    }

    private void ConfigureEntraId(string? tenantId, string? clientId)
    {
        // Users sign in with User.Impersonation only: their role decides what they may do, not delegated scopes.
        Implicit = new OpenApiOAuthFlow
        {
            AuthorizationUrl = new Uri(string.Format(AUTHORIZATION_URL, tenantId)),
            RefreshUrl = new Uri(string.Format(TOKEN_URL, tenantId)),
            Scopes =
            {
                { $"api://{clientId}/{Scopes.User.Impersonation}", "Allows the app to access the web API on your behalf" },
            },
        };

        // Machines get the app roles of their scopes.
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
