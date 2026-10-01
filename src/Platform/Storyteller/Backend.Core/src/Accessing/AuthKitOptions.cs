namespace _42.Platform.Storyteller.Accessing;

public class AuthKitOptions
{
    public string ClientId { get; set; } = string.Empty;

    // Or the custom auth domain. User tokens are checked against this issuer exactly.
    public string Issuer { get; set; } = "https://api.workos.com/";

    // Null uses https://api.workos.com/sso/jwks/{ClientId}.
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

    // WorkOS permission slug => Storyteller scope.
    public Dictionary<string, string> PermissionMap { get; set; } = new();

    // Phase E only.
    public string ApiKeyScheme { get; set; } = "WorkOS";
}
