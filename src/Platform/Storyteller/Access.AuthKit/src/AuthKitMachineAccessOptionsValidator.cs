using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Registered only with AddAuthKitMachineAccess: M2M applications need the management key,
// the AuthKit domain that issues their tokens, the environment client ID they are issued for,
// the organization that owns them and a PermissionMap that creation can look up one slug per scope in.
public sealed class AuthKitMachineAccessOptionsValidator : IValidateOptions<UserAuthenticationOptions>
{
    private const string UserImpersonation = "User.Impersonation";

    private static readonly string KnownScopes = $"{string.Join(", ", MachineScopes.ToScopeStrings(MachineScopes.All))} or {UserImpersonation}";

    public ValidateOptionsResult Validate(string? name, UserAuthenticationOptions options)
    {
        var authKit = options.AuthKit;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(authKit.ClientId))
        {
            failures.Add("Auth:AuthKit:ClientId (the M2M token audience) is required for AuthKit machine access.");
        }

        if (string.IsNullOrWhiteSpace(authKit.ApiKey))
        {
            failures.Add("Auth:AuthKit:ApiKey is required for AuthKit machine access.");
        }

        if (string.IsNullOrWhiteSpace(authKit.MachineOrganizationId))
        {
            failures.Add("Auth:AuthKit:MachineOrganizationId is required for AuthKit machine access.");
        }

        if (!Uri.TryCreate(authKit.AuthKitDomain, UriKind.Absolute, out var domain) || domain.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Auth:AuthKit:AuthKitDomain must be an absolute HTTPS URL for AuthKit machine access.");
        }

        ValidatePermissionMap(authKit.PermissionMap, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // Creation grants a scope through the one slug that maps it, so a value is exactly one scope string and no machine
    // scope has two slugs. A stale value such as Default.ReadWrite or "Annotation.Read Configuration.Read" fails here.
    // User.Impersonation serves user tokens only, so several permissions may give it.
    private static void ValidatePermissionMap(Dictionary<string, string> permissionMap, List<string> failures)
    {
        var slugsByScope = new Dictionary<MachineScope, string>();

        foreach (var (slug, value) in permissionMap.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var scopeString = value.Trim();

            if (MachineScopes.TryParseScopeString(scopeString, out var scope))
            {
                if (!slugsByScope.TryAdd(scope, slug))
                {
                    failures.Add(
                        $"Auth:AuthKit:PermissionMap maps both {slugsByScope[scope]} and {slug} to {scopeString}; a machine scope needs exactly one WorkOS permission.");
                }
            }
            else if (!string.Equals(scopeString, UserImpersonation, StringComparison.Ordinal))
            {
                failures.Add($"Auth:AuthKit:PermissionMap maps {slug} to '{value}', which is not exactly one of {KnownScopes}.");
            }
        }
    }
}
