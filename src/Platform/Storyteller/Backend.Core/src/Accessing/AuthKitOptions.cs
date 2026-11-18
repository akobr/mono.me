namespace _42.Platform.Storyteller.Accessing;

public class AuthKitOptions
{
    public string ClientId { get; set; } = string.Empty;

    // Or the custom auth domain. User tokens are checked against this issuer exactly.
    public string Issuer { get; set; } = "https://api.workos.com/";

    // Null uses {ApiBaseUrl}/sso/jwks/{ClientId}, which is https://api.workos.com/sso/jwks/{ClientId} by default.
    public string? JwksUri { get; set; }

    // The single audience of earlier releases, set when a JWT template adds "aud". Ignored when Audiences lists any;
    // GetAudiences() is the rule. Kept so that settings (and builds) that know only this key keep checking an audience.
    public string? Audience { get; set; }

    // The audiences of the 2S applications (WorkOS Multiple Applications): a user token must carry one of them. When it
    // lists any, Audience is ignored, so a shared audience of the environment can leave the check. No blank entries.
    public string[] Audiences { get; set; } = [];

    // https://<subdomain>.authkit.app or a custom domain. Used for Connect / M2M.
    public string AuthKitDomain { get; set; } = string.Empty;

    // sk_… management key. Resolved through Key Vault, never stored in plain settings.
    public string? ApiKey { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api.workos.com";

    // org_… that owns M2M applications.
    public string? MachineOrganizationId { get; set; }

    // Added to every user token. In practice only User.Impersonation: users are authorized by their role, and data
    // scopes in a user token are ignored.
    public string[] DefaultUserScopes { get; set; } = [];

    // Accepting an invitation needs a verified email. Tokens without an email_verified claim
    // count as verified only when this is false; add email_verified to the JWT template instead.
    public bool RequireVerifiedEmail { get; set; } = true;

    // WorkOS permission slug => exactly one scope string: a machine scope (Annotation.Read …) or User.Impersonation.
    // Slugs may contain ':'; AddUserAuthenticationOptions reads the section so they bind as one key.
    public Dictionary<string, string> PermissionMap { get; set; } = new();

    // Phase E only.
    public string ApiKeyScheme { get; set; } = "WorkOS";

    // The audiences a user token must carry one of: the non-blank Audiences, trimmed and distinct, when there are any;
    // otherwise a non-blank Audience, trimmed; otherwise none, and the audience is not checked.
    public IReadOnlyList<string> GetAudiences()
    {
        var audiences = GetListedAudiences();

        if (audiences.Length > 0)
        {
            return audiences;
        }

        return string.IsNullOrWhiteSpace(Audience) ? [] : [Audience.Trim()];
    }

    // True when Audiences overrides a non-blank Audience and still lists it, compared as IdentityModel matches aud:
    // ordinally, or with one trailing '/' more on one side.
    public bool ListsTheSingleAudience()
    {
        if (string.IsNullOrWhiteSpace(Audience))
        {
            return false;
        }

        var single = Audience.Trim();
        return GetListedAudiences().Any(audience => Matches(audience, single));

        static bool Matches(string first, string second)
        {
            if (string.Equals(first, second, StringComparison.Ordinal))
            {
                return true;
            }

            var (longer, shorter) = first.Length > second.Length ? (first, second) : (second, first);
            return longer.Length == shorter.Length + 1
                && longer.EndsWith('/')
                && string.Equals(longer[..^1], shorter, StringComparison.Ordinal);
        }
    }

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

    private string[] GetListedAudiences()
    {
        return Audiences
            .Where(audience => !string.IsNullOrWhiteSpace(audience))
            .Select(audience => audience.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
