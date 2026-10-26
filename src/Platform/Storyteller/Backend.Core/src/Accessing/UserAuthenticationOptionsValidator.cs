using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public sealed class UserAuthenticationOptionsValidator : IValidateOptions<UserAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, UserAuthenticationOptions options)
    {
        // Each provider has its own rules; the Entra ID ones apply to Entra ID only.
        return options.Provider switch
        {
            IdentityProviderKind.EntraId => ValidateEntraId(options),
            IdentityProviderKind.AuthKit => ValidateAuthKit(options.AuthKit),
            IdentityProviderKind.Keycloak => ValidateKeycloak(options),
            _ => ValidateOptionsResult.Fail($"Auth:Provider '{options.Provider}' is not a known identity provider."),
        };
    }

    private static ValidateOptionsResult ValidateEntraId(UserAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.TenantId))
        {
            return ValidateOptionsResult.Fail("Auth:ClientId and Auth:TenantId are required when Auth:Provider is EntraId.");
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateAuthKit(AuthKitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:ClientId is required when Auth:Provider is AuthKit.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:Issuer is required when Auth:Provider is AuthKit.");
        }

        // The key retriever only fetches over HTTPS. Failing here beats a 503 on every request.
        if (!Uri.TryCreate(options.GetJwksUri(), UriKind.Absolute, out var jwksUri)
            || jwksUri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:JwksUri (or Auth:AuthKit:ApiBaseUrl when JwksUri is empty) must be an absolute HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateKeycloak(UserAuthenticationOptions options)
    {
        var keycloak = options.Keycloak;
        var failures = new List<string>();

        // The metadata retriever fetches over HTTPS only, except from a local Keycloak (http://localhost:8080).
        if (!Uri.TryCreate(keycloak.GetIssuer(), UriKind.Absolute, out var issuer)
            || (issuer.Scheme != Uri.UriSchemeHttps && issuer.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Auth:Keycloak:Issuer must be the absolute URL of the realm ({server}/realms/{realm}) when Auth:Provider is Keycloak.");
        }
        else if (issuer.Scheme != Uri.UriSchemeHttps && !issuer.IsLoopback)
        {
            failures.Add("Auth:Keycloak:Issuer must use HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(keycloak.Audience))
        {
            failures.Add("Auth:Keycloak:Audience is required when Auth:Provider is Keycloak.");
        }

        // The list is the client gate of user tokens; the audience alone is a weak one in Keycloak (KeycloakUserOptions).
        if (!keycloak.AllowedClientIds.Any(clientId => !string.IsNullOrWhiteSpace(clientId)))
        {
            failures.Add("Auth:Keycloak:AllowedClientIds must list at least one client (azp) when Auth:Provider is Keycloak.");
        }
        else if (keycloak.AllowedClientIds.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Auth:Keycloak:AllowedClientIds entries must not be blank.");
        }
        else if (!string.IsNullOrWhiteSpace(keycloak.ClientId)
            && !keycloak.AllowedClientIds.Any(clientId => string.Equals(clientId.Trim(), keycloak.ClientId.Trim(), StringComparison.Ordinal)))
        {
            // The advertised sign-in client would get tokens the API refuses.
            failures.Add("Auth:Keycloak:ClientId must be one of Auth:Keycloak:AllowedClientIds.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
