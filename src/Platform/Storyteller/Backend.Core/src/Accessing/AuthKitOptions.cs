namespace _42.Platform.Storyteller.Accessing;

public class AuthKitOptions
{
    public string ClientId { get; set; } = string.Empty;

    // Or the custom auth domain. User tokens are checked against this issuer exactly.
    public string Issuer { get; set; } = "https://api.workos.com/";

    // Null uses {ApiBaseUrl}/sso/jwks/{ClientId}, which is https://api.workos.com/sso/jwks/{ClientId} by default.
    public string? JwksUri { get; set; }

    // Set when a JWT template adds "aud". Null means the audience is not validated.
    public string? Audience { get; set; }

    // https://<subdomain>.authkit.app or a custom domain. Used for Connect / M2M.
    public string AuthKitDomain { get; set; } = string.Empty;

    // sk_… management key. Resolved through Key Vault, never stored in plain settings.
    public string? ApiKey { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api.workos.com";

    // org_… that owns M2M applications.
    public string? MachineOrganizationId { get; set; }

    public string[] DefaultUserScopes { get; set; } = [];

    // Accepting an invitation needs a verified email. Tokens without an email_verified claim
    // count as verified only when this is false; add email_verified to the JWT template instead.
    public bool RequireVerifiedEmail { get; set; } = true;

    // WorkOS permission slug => Storyteller scope. A value can hold several scopes separated by spaces.
    // Slugs may contain ':'; AddUserAuthenticationOptions reads the section so they bind as one key.
    public Dictionary<string, string> PermissionMap { get; set; } = new();

    // Phase E only.
    public string ApiKeyScheme { get; set; } = "WorkOS";

    public string GetJwksUri()
    {
        return string.IsNullOrWhiteSpace(JwksUri)
            ? $"{ApiBaseUrl.TrimEnd('/')}/sso/jwks/{Uri.EscapeDataString(ClientId)}"
            : JwksUri;
    }

    // M2M machine access needs both the AuthKit domain (token issuer) and the organization that owns
    // the applications. AuthKitDomain alone only describes the OpenAPI flows.
    public bool HasMachineAccess()
    {
        return !string.IsNullOrWhiteSpace(AuthKitDomain) && !string.IsNullOrWhiteSpace(MachineOrganizationId);
    }

    public string GetMachineJwksUri()
    {
        return $"{AuthKitDomain.TrimEnd('/')}/oauth2/jwks";
    }
}
