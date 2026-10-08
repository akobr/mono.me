using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Maps AuthKit user access-token claims to the Entra-shaped set the API helpers read:
// name, preferred_username, and a space-separated scp built only from configuration.
public sealed class AuthKitClaimNormalizer : IBearerClaimsNormalizer
{
    private const string ScopeClaimType = "scp";
    private const string NameClaimType = "name";
    private const string UserNameClaimType = "preferred_username";
    private const string PermissionsClaimType = "permissions";

    private static readonly char[] ScopeSeparators = [' ', '\t', ','];

    private readonly string[] _defaultScopes;
    private readonly IReadOnlyDictionary<string, string> _permissionMap;

    public AuthKitClaimNormalizer(IOptions<UserAuthenticationOptions> options)
    {
        var authKit = options.Value.AuthKit;
        _defaultScopes = authKit.DefaultUserScopes;
        _permissionMap = new Dictionary<string, string>(authKit.PermissionMap, StringComparer.Ordinal);
    }

    public BearerValidationResult Normalize(IReadOnlyList<Claim> claims)
    {
        var normalized = new List<Claim>(claims.Count + 3);

        foreach (var claim in claims)
        {
            if (!IsReplaced(claim.Type))
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

        return new BearerValidationResult(normalized, IsMachine: false, MachineId: null);
    }

    // Joins the non-blank parts with a space. Null when every part is blank.
    internal static string? JoinNames(params string?[] parts)
    {
        var joined = string.Join(' ', parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));
        return joined.Length > 0 ? joined : null;
    }

    // Scope-bearing claims are rebuilt from configuration, so a JWT template cannot grant scopes
    // directly. azp is dropped because machine identity comes from the validator, not the token.
    private static bool IsReplaced(string claimType)
    {
        return claimType is ScopeClaimType or "roles" or ClaimTypes.Role or "azp" or NameClaimType or UserNameClaimType
            || claimType.EndsWith("/scope", StringComparison.Ordinal);
    }

    // A JWT template such as "{{ user.first_name }} {{ user.last_name }}" renders missing
    // values as empty strings, so a blank or space-only name falls through to the next source.
    private static string? ResolveName(IReadOnlyList<Claim> claims)
    {
        return FirstValue(claims, NameClaimType)
            ?? JoinName(claims, "first_name", "last_name")
            ?? JoinName(claims, "given_name", "family_name")
            ?? FirstValue(claims, "email");
    }

    private static string? JoinName(IReadOnlyList<Claim> claims, string firstType, string lastType)
    {
        return JoinNames(FirstValue(claims, firstType), FirstValue(claims, lastType));
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

    private List<string> ResolveScopes(IReadOnlyList<Claim> claims)
    {
        var scopes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scope in _defaultScopes)
        {
            AddScopes(scope);
        }

        foreach (var claim in claims)
        {
            // Unmapped WorkOS permissions grant nothing.
            if (claim.Type == PermissionsClaimType
                && _permissionMap.TryGetValue(claim.Value, out var mapped))
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
