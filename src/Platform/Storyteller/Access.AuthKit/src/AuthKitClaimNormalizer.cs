using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Maps AuthKit access-token claims to the Entra-shaped set the API helpers read:
// name, preferred_username, and a space-separated scp built only from configuration.
// User tokens get DefaultUserScopes plus mapped permissions; M2M tokens only their mapped scopes.
public sealed class AuthKitClaimNormalizer : IBearerClaimsNormalizer
{
    private const string ScopeClaimType = "scp";
    private const string NameClaimType = "name";
    private const string UserNameClaimType = "preferred_username";
    private const string PermissionsClaimType = "permissions";
    private const string OAuthScopeClaimType = "scope";
    private const string ClientIdClaimType = "client_id";
    private const string UserIdPrefix = "user_";

    private static readonly char[] ScopeSeparators = [' ', '\t', ','];

    private readonly string[] _defaultScopes;
    private readonly IReadOnlyDictionary<string, string> _permissionMap;

    public AuthKitClaimNormalizer(IOptions<UserAuthenticationOptions> options)
    {
        var authKit = options.Value.AuthKit;
        _defaultScopes = authKit.DefaultUserScopes;
        _permissionMap = new Dictionary<string, string>(authKit.PermissionMap, StringComparer.Ordinal);
    }

    // Validators call NormalizeUser or NormalizeMachine for the token source they checked.
    // This entry point serves DEV_AUTH decoding, where the source is unknown.
    public BearerValidationResult Normalize(IReadOnlyList<Claim> claims)
    {
        var clientId = TryGetMachineClientId(claims);
        return clientId is null ? NormalizeUser(claims) : NormalizeMachine(claims, clientId);
    }

    public BearerValidationResult NormalizeUser(IReadOnlyList<Claim> claims)
    {
        var normalized = CopyKeptClaims(claims);

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

        AddScopeClaim(normalized, ResolveScopes(claims, _defaultScopes, PermissionsClaimType));
        return new BearerValidationResult(normalized, IsMachine: false, MachineId: null);
    }

    // M2M tokens carry granted WorkOS permission slugs in a space-separated scope claim.
    public BearerValidationResult NormalizeMachine(IReadOnlyList<Claim> claims, string clientId)
    {
        var normalized = CopyKeptClaims(claims);
        normalized.Add(new Claim("azp", clientId));
        AddScopeClaim(normalized, ResolveScopes(claims, [], OAuthScopeClaimType, PermissionsClaimType));
        return new BearerValidationResult(normalized, IsMachine: true, MachineId: clientId);
    }

    // An M2M token names its application in client_id, and sub is that same client ID.
    // Null for user tokens, including Connect user tokens that also carry client_id.
    public static string? TryGetMachineClientId(IReadOnlyList<Claim> claims)
    {
        var clientId = FirstValue(claims, ClientIdClaimType);
        var subject = FirstValue(claims, "sub");

        return clientId is not null
            && string.Equals(subject, clientId, StringComparison.Ordinal)
            && !clientId.StartsWith(UserIdPrefix, StringComparison.Ordinal)
                ? clientId
                : null;
    }

    // Joins the non-blank parts with a space. Null when every part is blank.
    internal static string? JoinNames(params string?[] parts)
    {
        var joined = string.Join(' ', parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));
        return joined.Length > 0 ? joined : null;
    }

    internal static IEnumerable<string> SplitScopes(string value)
    {
        return value.Split(ScopeSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // Scope-bearing claims are rebuilt from configuration, so a JWT template cannot grant scopes
    // directly. azp is dropped because machine identity comes from the validator, not the token.
    private static bool IsReplaced(string claimType)
    {
        return claimType is ScopeClaimType or "roles" or ClaimTypes.Role or "azp" or NameClaimType or UserNameClaimType
            || claimType.EndsWith("/scope", StringComparison.Ordinal);
    }

    private static List<Claim> CopyKeptClaims(IReadOnlyList<Claim> claims)
    {
        var kept = new List<Claim>(claims.Count + 3);

        foreach (var claim in claims)
        {
            if (!IsReplaced(claim.Type))
            {
                kept.Add(claim);
            }
        }

        return kept;
    }

    private static void AddScopeClaim(List<Claim> claims, List<string> scopes)
    {
        if (scopes.Count > 0)
        {
            claims.Add(new Claim(ScopeClaimType, string.Join(' ', scopes)));
        }
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

    private List<string> ResolveScopes(IReadOnlyList<Claim> claims, IEnumerable<string> defaultScopes, params string[] slugClaimTypes)
    {
        var scopes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scope in defaultScopes)
        {
            AddScopes(scope);
        }

        foreach (var claim in claims)
        {
            if (!slugClaimTypes.Contains(claim.Type))
            {
                continue;
            }

            // A scope claim is one space-separated string; permissions arrive one slug per claim.
            foreach (var slug in SplitScopes(claim.Value))
            {
                // Unmapped WorkOS permissions grant nothing.
                if (_permissionMap.TryGetValue(slug, out var mapped))
                {
                    AddScopes(mapped);
                }
            }
        }

        return scopes;

        void AddScopes(string value)
        {
            foreach (var scope in SplitScopes(value))
            {
                if (seen.Add(scope))
                {
                    scopes.Add(scope);
                }
            }
        }
    }
}
