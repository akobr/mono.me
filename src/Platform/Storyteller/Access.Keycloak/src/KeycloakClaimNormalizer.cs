using System.Security.Claims;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Maps Keycloak user access-token claims to the set the API helpers read: sub (the account id), name,
// preferred_username, and a space-separated scp built only from configuration: DefaultUserScopes plus what RoleMap
// gives the realm roles (realm_access.roles, by name) and the client roles (resource_access.{client}.roles, as
// {client}:{role}) of the token.
public sealed class KeycloakClaimNormalizer : IBearerClaimsNormalizer
{
    private const string SubjectClaimType = "sub";
    private const string ScopeClaimType = "scp";
    private const string NameClaimType = "name";
    private const string UserNameClaimType = "preferred_username";
    private const string ClientClaimType = "azp";
    private const string ClientIdClaimType = "client_id";
    private const string LegacyClientIdClaimType = "clientId";
    private const string RealmAccessClaimType = "realm_access";
    private const string ResourceAccessClaimType = "resource_access";
    private const string RolesProperty = "roles";
    private const string ServiceAccountUserNamePrefix = "service-account-";

    private static readonly char[] ScopeSeparators = [' ', '\t', ','];

    private readonly string[] _defaultScopes;
    private readonly IReadOnlyDictionary<string, string> _roleMap;

    public KeycloakClaimNormalizer(IOptions<UserAuthenticationOptions> options)
    {
        var keycloak = options.Value.Keycloak;
        _defaultScopes = keycloak.DefaultUserScopes;
        _roleMap = new Dictionary<string, string>(keycloak.RoleMap, StringComparer.Ordinal);
    }

    // KeycloakBearerTokenValidator calls NormalizeUser for the user tokens it checked. This entry point serves
    // DEV_AUTH decoding, where a service account is the machine of its client (azp), as KeycloakMachineTokenValidator
    // has it. A service account that names no client keeps no sub, so GetIdentityUniqueId answers 401.
    public BearerValidationResult Normalize(IReadOnlyList<Claim> claims)
    {
        if (!IsServiceAccount(claims))
        {
            return NormalizeUser(claims);
        }

        var clientId = FirstValue(claims, ClientClaimType)
            ?? FirstValue(claims, ClientIdClaimType)
            ?? FirstValue(claims, LegacyClientIdClaimType);

        return clientId is not null
            ? KeycloakMachineTokenValidator.Normalize(claims, clientId)
            : new BearerValidationResult(BuildUserClaims(claims, keepSubject: false), IsMachine: false, MachineId: null);
    }

    public BearerValidationResult NormalizeUser(IReadOnlyList<Claim> claims)
    {
        return new BearerValidationResult(BuildUserClaims(claims, keepSubject: true), IsMachine: false, MachineId: null);
    }

    // The token of a client's service account names the client in client_id (clientId in older Keycloak versions),
    // and its user is service-account-{client}. Such a token is never a user.
    public static bool IsServiceAccount(IReadOnlyList<Claim> claims)
    {
        return claims.Any(claim => claim.Type is ClientIdClaimType or LegacyClientIdClaimType)
            || FirstValue(claims, UserNameClaimType)?.StartsWith(ServiceAccountUserNamePrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    // The realm roles by name and the client roles as {client}:{role}, in token order. Claims that are not the JSON
    // objects Keycloak writes grant nothing.
    private static List<string> ReadRoles(IReadOnlyList<Claim> claims)
    {
        var roles = new List<string>();

        foreach (var claim in claims)
        {
            if (claim.Type is not (RealmAccessClaimType or ResourceAccessClaimType))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(claim.Value);

                if (claim.Type == RealmAccessClaimType)
                {
                    AddRoles(document.RootElement, prefix: string.Empty, roles);
                }
                else if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var client in document.RootElement.EnumerateObject())
                    {
                        AddRoles(client.Value, $"{client.Name}:", roles);
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON: no roles.
            }
        }

        return roles;
    }

    private static void AddRoles(JsonElement access, string prefix, List<string> roles)
    {
        if (access.ValueKind != JsonValueKind.Object
            || !access.TryGetProperty(RolesProperty, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } role)
            {
                roles.Add(prefix + role);
            }
        }
    }

    // Scope-bearing claims are rebuilt from configuration, so a token mapper cannot grant scopes directly.
    private static bool IsReplaced(string claimType)
    {
        return claimType is ScopeClaimType or "roles" or ClaimTypes.Role or NameClaimType or UserNameClaimType
            || claimType.EndsWith("/scope", StringComparison.Ordinal);
    }

    // A blank part (a user without a family name) does not leave a stray space.
    private static string? ResolveName(IReadOnlyList<Claim> claims)
    {
        var parts = new[] { FirstValue(claims, "given_name"), FirstValue(claims, "family_name") }
            .Where(part => part is not null)
            .ToArray();

        return FirstValue(claims, NameClaimType)
            ?? (parts.Length > 0 ? string.Join(' ', parts) : null)
            ?? FirstValue(claims, UserNameClaimType)
            ?? FirstValue(claims, "email");
    }

    private static string? FirstValue(IReadOnlyList<Claim> claims, string claimType)
    {
        foreach (var claim in claims)
        {
            if (claim.Type == claimType && !string.IsNullOrWhiteSpace(claim.Value))
            {
                return claim.Value.Trim();
            }
        }

        return null;
    }

    private List<Claim> BuildUserClaims(IReadOnlyList<Claim> claims, bool keepSubject)
    {
        var normalized = new List<Claim>(claims.Count + 3);

        foreach (var claim in claims)
        {
            if (!IsReplaced(claim.Type) && (keepSubject || claim.Type is not (SubjectClaimType or ClaimTypes.NameIdentifier)))
            {
                normalized.Add(claim);
            }
        }

        var name = ResolveName(claims);
        if (name is not null)
        {
            normalized.Add(new Claim(NameClaimType, name));
        }

        var userName = FirstValue(claims, UserNameClaimType) ?? FirstValue(claims, "email");
        if (userName is not null)
        {
            normalized.Add(new Claim(UserNameClaimType, userName));
        }

        var scopes = ResolveScopes(claims);
        if (scopes.Count > 0)
        {
            normalized.Add(new Claim(ScopeClaimType, string.Join(' ', scopes)));
        }

        return normalized;
    }

    private List<string> ResolveScopes(IReadOnlyList<Claim> claims)
    {
        var scopes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scope in _defaultScopes)
        {
            AddScopes(scope);
        }

        foreach (var role in ReadRoles(claims))
        {
            // Unmapped roles (default-roles-{realm}, offline_access, account:manage-account …) grant nothing.
            if (_roleMap.TryGetValue(role, out var mapped))
            {
                AddScopes(mapped);
            }
        }

        return scopes;

        void AddScopes(string value)
        {
            foreach (var scope in value.Split(ScopeSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(scope))
                {
                    scopes.Add(scope);
                }
            }
        }
    }
}
