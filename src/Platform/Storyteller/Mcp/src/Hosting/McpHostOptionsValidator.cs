using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.Hosting;

// Fails the start on settings that would let the server accept tokens without a pinned issuer, without a known client
// under Entra ID, or over plain HTTP outside Development, and on an actor of the token exchange without exactly one
// credential (a client secret only in Development) or with a managed identity token for another service's audience,
// which would hand the API a credential of that service.
public sealed class McpHostOptionsValidator(IHostEnvironment environment) : IValidateOptions<McpHostOptions>
{
    // The audiences whose tokens are credentials of another service: the Entra and Azure part of Backend.Core's
    // FederationValidation.ForeignAudiences, which this project does not reference. Its AWS and Kubernetes audiences
    // cannot be asked of a managed identity.
    private static readonly string[] ForeignAudiences =
    [
        "api://AzureADTokenExchange",
        "https://management.azure.com",
        "https://management.core.windows.net",
        "https://graph.microsoft.com",
        "https://vault.azure.net",
        "https://storage.azure.com",
        "https://database.windows.net",
    ];

    public ValidateOptionsResult Validate(string? name, McpHostOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            return ValidateOptionsResult.Fail($"Mcp:Provider '{options.Provider}' is not a known identity provider: use EntraId, AuthKit or Keycloak.");
        }

        var failures = new List<string>();
        var authentication = options.Authentication;

        if (!McpUrls.IsValid(options.ResourceUrl, environment, out var resourceProblem))
        {
            failures.Add($"Mcp:ResourceUrl {resourceProblem}");
        }

        if (options.Provider == McpIdentityProvider.EntraId)
        {
            ValidateEntraId(authentication, failures);
        }
        else
        {
            ValidateIssuerProvider(options.Provider, authentication, failures);
        }

        if (string.IsNullOrWhiteSpace(authentication.Scope) || authentication.Scope.Contains(' ', StringComparison.Ordinal))
        {
            failures.Add("Mcp:Authentication:Scope must be one scope name, for example Mcp.Access.");
        }

        if (!string.IsNullOrWhiteSpace(authentication.Audience) && authentication.Audience.EndsWith('/'))
        {
            failures.Add("Mcp:Authentication:Audience must not end with a slash.");
        }

        ValidateActor(options.Actor, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // One tenant and listed client apps: Entra ID has no dynamic client registration.
    private static void ValidateEntraId(McpAuthenticationOptions authentication, List<string> failures)
    {
        var hasTenant = Guid.TryParse(authentication.TenantId, out var tenantId);

        if (!hasTenant)
        {
            failures.Add("Mcp:Authentication:TenantId must be the GUID of the tenant (not common or organizations).");
        }

        if (!Guid.TryParse(authentication.ClientId, out _))
        {
            failures.Add("Mcp:Authentication:ClientId must be the GUID of the MCP app registration.");
        }

        if (authentication.AllowedClientIds.Count == 0 || authentication.AllowedClientIds.Any(id => !Guid.TryParse(id, out _)))
        {
            failures.Add("Mcp:Authentication:AllowedClientIds must list the GUIDs of the MCP client apps whose tokens the server accepts.");
        }

        // The issuers come from the tenant; an issuer that names another one is a mistake, not a choice.
        if (!string.IsNullOrWhiteSpace(authentication.Issuer)
            && hasTenant
            && !string.Equals(authentication.Issuer.TrimEnd('/'), McpAuthSettings.GetEntraIssuer(tenantId), StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Mcp:Authentication:Issuer must be empty or {McpAuthSettings.GetEntraIssuer(tenantId)} under Entra ID: the issuers come from Mcp:Authentication:TenantId.");
        }
    }

    // The actor of the API's token exchange: its id and exactly one credential, each with the values it needs and none
    // of another kind's, so a setting that does not apply is never ignored silently.
    private void ValidateActor(McpActorOptions actor, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(actor.Id))
        {
            failures.Add("Mcp:Actor:Id must name the actor of the Storyteller API's Auth:TokenExchange:Actors, for example mcp.");
        }

        var credentials = (actor.HasWorkloadIdentity ? 1 : 0) + (actor.HasClientAssertionKey ? 1 : 0) + (actor.HasClientSecret ? 1 : 0);

        if (credentials != 1)
        {
            failures.Add("Set exactly one credential of the actor: Mcp:Actor:WorkloadIdentity, Mcp:Actor:ClientAssertionKey or Mcp:Actor:ClientSecret.");
        }

        if (actor.HasClientSecret && !environment.IsDevelopment())
        {
            failures.Add("Mcp:Actor:ClientSecret is accepted in the Development environment only; use Mcp:Actor:WorkloadIdentity or Mcp:Actor:ClientAssertionKey.");
        }

        if (actor.HasWorkloadIdentity)
        {
            ValidateWorkloadIdentity(actor.WorkloadIdentity!, failures);
        }

        if (actor.HasClientAssertionKey)
        {
            ValidateClientAssertionKey(actor.ClientAssertionKey!, failures);
        }
    }

    // The API compares the assertion's aud exactly (Ordinal) and its issuer is the operator's choice, so the audience is
    // held to an absolute URI only, not to the rules of McpUrls. A path is no URI here, although Unix parses it as a file.
    private static void ValidateClientAssertionKey(McpClientAssertionKeyOptions key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(key.CertificatePath))
        {
            failures.Add("Mcp:Actor:ClientAssertionKey:CertificatePath must name the PKCS#12 file with the RSA key that signs the client assertions.");
        }

        if (!string.IsNullOrWhiteSpace(key.Audience)
            && (key.Audience != key.Audience.Trim() || !Uri.TryCreate(key.Audience, UriKind.Absolute, out var audience) || audience.IsFile))
        {
            failures.Add("Mcp:Actor:ClientAssertionKey:Audience must be empty or the API's token endpoint ({MachineAuth:Federation:PublicBaseUrl}/v1/access/token) or its MachineAuth:Federation:Issuer, exactly as the API has them.");
        }
    }

    // Trimmed, with one trailing slash removed, and in any case.
    private static bool IsForeignAudience(string audience)
    {
        var trimmed = audience.Trim();
        trimmed = trimmed.EndsWith('/') ? trimmed[..^1] : trimmed;
        return ForeignAudiences.Any(foreign => string.Equals(trimmed, foreign, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateWorkloadIdentity(McpWorkloadIdentityOptions identity, List<string> failures)
    {
        if (identity.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            failures.Add("Mcp:Actor:WorkloadIdentity:Kind must be ManagedIdentity or TokenFile.");
            return;
        }

        if (kind == McpWorkloadIdentityKind.ManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(identity.Audience))
            {
                failures.Add("Mcp:Actor:WorkloadIdentity:Audience must be the audience the API's actor expects: the App ID URI of an app registration only Storyteller accepts, for example api://storyteller-token-exchange.");
            }
            else if (identity.Audience.Trim().EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Mcp:Actor:WorkloadIdentity:Audience is the audience itself, without /.default.");
            }
            else if (IsForeignAudience(identity.Audience))
            {
                failures.Add($"Mcp:Actor:WorkloadIdentity:Audience {identity.Audience} belongs to another service: a token for it is a credential there (an api://AzureADTokenExchange token signs in as every app that trusts this identity). Use an audience only Storyteller accepts, for example api://storyteller-token-exchange.");
            }

            if (!string.IsNullOrWhiteSpace(identity.TokenFilePath))
            {
                failures.Add("Mcp:Actor:WorkloadIdentity:TokenFilePath applies to the TokenFile kind only.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(identity.TokenFilePath))
        {
            failures.Add("Mcp:Actor:WorkloadIdentity:TokenFilePath must name the token file of the TokenFile kind.");
        }

        if (!string.IsNullOrWhiteSpace(identity.Audience) || !string.IsNullOrWhiteSpace(identity.ClientId))
        {
            failures.Add("Mcp:Actor:WorkloadIdentity:Audience and ClientId apply to the ManagedIdentity kind only; the token file carries its own audience.");
        }
    }

    // AuthKit and Keycloak: the issuer is the authorization server MCP clients find in the resource metadata.
    private void ValidateIssuerProvider(McpIdentityProvider provider, McpAuthenticationOptions authentication, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(authentication.Issuer))
        {
            failures.Add(provider == McpIdentityProvider.AuthKit
                ? "Mcp:Authentication:Issuer must be the AuthKit domain, for example https://auth.example.com."
                : "Mcp:Authentication:Issuer must be the Keycloak realm, for example https://keycloak.example.com/realms/storyteller.");
        }
        else if (!McpUrls.IsValid(authentication.Issuer, environment, out var issuerProblem))
        {
            failures.Add($"Mcp:Authentication:Issuer {issuerProblem}");
        }

        if (!string.IsNullOrWhiteSpace(authentication.TenantId))
        {
            failures.Add($"Mcp:Authentication:TenantId applies to Entra ID only; leave it empty under {provider}.");
        }

        if (!string.IsNullOrWhiteSpace(authentication.ClientId))
        {
            failures.Add($"Mcp:Authentication:ClientId applies to Entra ID only; leave it empty under {provider}.");
        }

        if (authentication.AllowedClientIds.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Mcp:Authentication:AllowedClientIds must not contain empty client ids.");
        }
    }
}
