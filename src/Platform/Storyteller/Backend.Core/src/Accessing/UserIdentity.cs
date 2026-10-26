using System.Security.Claims;

namespace _42.Platform.Storyteller.Accessing;

// The id of a person's account under the deployment's one identity provider. Entra ID keys accounts by {tid}:{oid}:
// sub is pairwise per application, oid is the same in every app of the tenant, and the tenant keeps tenants apart.
// AuthKit and Keycloak key them by sub, the user id of the environment or the realm.
public static class UserIdentity
{
    // The pairwise sub of an Entra ID token, kept next to the account id that replaces it as sub.
    public const string EntraSubjectClaimType = "entra_sub";

    private const string SubjectClaimType = "sub";
    private const string TenantIdClaimType = "tid";
    private const string ObjectIdClaimType = "oid";
    private const string MappedTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string MappedObjectIdClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    // A {guid}:{guid} pair in the dashed form (D).
    private const int GuidLength = 36;

    // Null when the claims name no account: an Entra ID token without a GUID tid and oid, or a token without sub.
    // Validated Entra claims arrive mapped (JwtSecurityTokenHandler), decoded claims raw: both names are read.
    public static string? GetAccountId(IReadOnlyList<Claim> claims, IdentityProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return provider switch
        {
            IdentityProviderKind.EntraId => GetEntraAccountId(claims),
            IdentityProviderKind.AuthKit or IdentityProviderKind.Keycloak => GetSubject(claims),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Not a known identity provider."),
        };
    }

    // The form GetAccountId gives Entra ID accounts: two GUIDs in the lower-case dashed form, joined by ':'.
    public static bool IsEntraAccountId(string? accountId)
    {
        return accountId is { Length: (GuidLength * 2) + 1 }
            && accountId[GuidLength] == ':'
            && IsLowerCaseGuid(accountId.AsSpan(0, GuidLength))
            && IsLowerCaseGuid(accountId.AsSpan(GuidLength + 1));
    }

    private static string? GetEntraAccountId(IReadOnlyList<Claim> claims)
    {
        var tenantId = FirstValue(claims, TenantIdClaimType) ?? FirstValue(claims, MappedTenantIdClaimType);
        var objectId = FirstValue(claims, ObjectIdClaimType) ?? FirstValue(claims, MappedObjectIdClaimType);

        return Guid.TryParse(tenantId, out var tenant) && Guid.TryParse(objectId, out var person)
            ? $"{tenant:D}:{person:D}"
            : null;
    }

    private static string? GetSubject(IReadOnlyList<Claim> claims)
    {
        var subject = (FirstValue(claims, SubjectClaimType) ?? FirstValue(claims, ClaimTypes.NameIdentifier))?.Trim();
        return string.IsNullOrEmpty(subject) ? null : subject;
    }

    private static bool IsLowerCaseGuid(ReadOnlySpan<char> value)
    {
        return Guid.TryParseExact(value, "D", out _) && !value.ContainsAnyInRange('A', 'F');
    }

    private static string? FirstValue(IReadOnlyList<Claim> claims, string type)
    {
        foreach (var claim in claims)
        {
            if (claim.Type == type)
            {
                return claim.Value;
            }
        }

        return null;
    }
}
