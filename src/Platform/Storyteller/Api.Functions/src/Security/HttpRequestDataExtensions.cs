using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;

using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;

using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.Security;

// The outcome of GetSecretRightsAsync: the caller, and whether secrets were withheld because the machine has no mTLS.
public sealed record SecretRights(KeyValueCaller Caller, bool IsWithheldByMtls)
{
    public const string WithheldHeader = "X-2S-Secrets-Withheld";

    public const string WithheldByMtls = "mtls-required";

    public bool CanReadSecrets => Caller.CanReadSecrets;

    // Makes the caller the ambient one of the request, for the paths without a caller parameter (binding reads).
    // Called by the handler itself: an AsyncLocal set inside an awaited method would not reach the handler's later calls.
    public SecretRights AsAmbient()
    {
        KeyValueCallerContext.Current = Caller;
        return this;
    }
}

public static class HttpRequestDataExtensions
{
    // Debug builds skip the scope and role checks (DEV_AUTH); tests of those checks look at this.
#if DEV_AUTH
    public const bool IsDevelopmentAuthentication = true;
#else
    public const bool IsDevelopmentAuthentication = false;
#endif

    private const string NameClaimType = "name";

    private static readonly string[] UniqueNameClaimTypes = ["preferred_username", "unique_name", ClaimTypes.Upn];

    public static IReadOnlyList<Claim> GetClaims(this HttpRequestData @this)
    {
        @this.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.CachedClaims, out var claimMap);

        if (claimMap is IReadOnlyList<Claim> cachedClaims)
        {
            return cachedClaims;
        }

        var identity = @this.Identities.FirstOrDefault(i => i.IsAuthenticated);

        if (identity is null)
        {
            return [];
        }

        var claims = identity.Claims.ToList();
        @this.FunctionContext.Items[FunctionContextItemKeys.CachedClaims] = claims;
        return claims;
    }

    // Every scope the caller's claims grant, as CheckScope reads them.
    public static HashSet<string> GetScopes(this HttpRequestData @this)
    {
        return @this.GetClaims()
            .Where(c => c.Type is "scp" or "roles" or ClaimTypes.Role || c.Type.EndsWith("/scope"))
            .SelectMany(c => c.Value.Split(' '))
            .Select(scope => scope.StartsWith("App.", StringComparison.OrdinalIgnoreCase) ? scope[4..] : scope)
            .ToHashSet();
    }

    public static void CheckScope(this HttpRequestData @this, params string[] scopes)
    {
#if DEV_AUTH
        return;
#endif
        var claims = @this.GetClaims();
        var allScopes = @this.GetScopes();

        if (!scopes.Any(scope => allScopes.Contains(scope)))
        {
            // TODO: [P2] remove details from the exception message
            var allInfo = new StringBuilder();
            allInfo.AppendLine($"all available scopes: {string.Join(", ", allScopes)}");
            allInfo.AppendLine($"all claims: {string.Join("; ", claims.Select(c => $"{c.Type}={c.Value}"))}");
            allInfo.AppendLine($"identities: {string.Join("; ", @this.Identities.Select(i => $"{i.Name}|{i.IsAuthenticated}|{i.AuthenticationType}"))}");
            allInfo.AppendLine("Missing scope(s): " + string.Join(", ", scopes));
            throw new SecurityTokenException(allInfo.ToString());
        }
    }

    public static bool TryCheckScope(this HttpRequestData @this, string scope)
    {
#if DEV_AUTH
        return true;
#endif
        return @this.GetScopes().Contains(scope);
    }

    // Who makes the request, for every audit stamp: the machine, or the account (its sub) with a display name from
    // the token. The profile resolver is not called, so recording the actor adds no latency. Debug builds (DEV_AUTH)
    // also serve requests without an identity; those are System.
    public static AuditActor GetAuditActor(this HttpRequestData @this)
    {
        if (@this.TryGetApplicationIdentity(out var appId))
        {
            return AuditActor.Machine(appId);
        }

#if DEV_AUTH
        if (string.IsNullOrWhiteSpace(@this.GetClaim("sub", ClaimTypes.NameIdentifier)))
        {
            return AuditActor.System;
        }
#endif

        var name = @this.GetClaim(NameClaimType)?.Trim();
        var userName = @this.GetClaim(UniqueNameClaimTypes)?.Trim();
        return AuditActor.Account(
            @this.GetIdentityUniqueId(),
            string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(userName) ? null : userName) : name);
    }

    // Who the caller is for key/value secrets, and whether it may receive secret values: a user needs
    // ContributorWithSecrets (or the Configuration.Secrets scope), a machine needs the scope and, when the project
    // says so, an mTLS certificate. Handlers call AsAmbient() on the result before they resolve bindings.
    public static async Task<SecretRights> GetSecretRightsAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        IKeyValuePolicyStore policies,
        string organization,
        string project)
    {
        var actor = @this.GetAuditActor();
        var correlationId = @this.FunctionContext.InvocationId;
        var hasSecretScope = @this.TryCheckScope(Scopes.Configuration.Secrets);

        if (@this.TryGetApplicationIdentity(out _))
        {
            var channel = @this.FunctionContext.Items.ContainsKey(FunctionContextItemKeys.PresentingCertificateThumbprint)
                ? KeyValueChannel.MachineMtls
                : KeyValueChannel.MachineToken;
            var policy = await policies.GetAsync(organization, project);
            var withheld = hasSecretScope && policy.RequireMtlsForSecretDelivery && channel != KeyValueChannel.MachineMtls;
            var allowed = hasSecretScope && !withheld;
            var machine = new KeyValueCaller(actor, allowed, allowed, channel) { CorrelationId = correlationId };
            return new SecretRights(machine, withheld);
        }

        var canUseSecrets = hasSecretScope;

        if (!canUseSecrets && @this.TryCheckScope(Scopes.User.Impersonation))
        {
            var accountId = @this.GetIdentityUniqueId();
            var accessRole = await accessService.GetAccountRoleAsync(accountId, $"{organization}.{project}");
            canUseSecrets = accessRole >= AccountRole.ContributorWithSecrets;
        }

        var user = new KeyValueCaller(actor, canUseSecrets, canUseSecrets, KeyValueChannel.User) { CorrelationId = correlationId };
        return new SecretRights(user, IsWithheldByMtls: false);
    }

    // Response headers through the ASP.NET Core integration; a test context without it is left alone.
    public static void SetResponseHeader(this HttpRequestData @this, string name, string value)
    {
        var httpContext = @this.FunctionContext.GetHttpContext();

        if (httpContext is not null)
        {
            httpContext.Response.Headers[name] = value;
        }
    }

    // Responses that carry a secret value are never cached.
    public static void MarkNoStore(this HttpRequestData @this)
    {
        @this.SetResponseHeader("Cache-Control", "no-store");
    }

    public static async Task CheckAccessToAsync(this HttpRequestData @this, IAccessService accessService, string accessPointKey, AccountRole minimalRole = AccountRole.Reader)
    {
#if DEV_AUTH
        return;
#else
        if (@this.TryGetApplicationIdentity(out var appId))
        {
            var segments = accessPointKey.Split('.', StringSplitOptions.None);

            if (segments.Length < 2)
            {
                throw new SecurityTokenException("An application can never access an organization.");
            }

            if (FederatedMachineAccess.IsFederated(@this.FunctionContext))
            {
                // One point read of the machine replaces the existence check: the kind and the scope count too.
                await FederatedMachineAccess.EnsureAllowedAsync(@this, accessService, segments[0], segments[1], appId);
            }
            else if (!await accessService.VerifyAccessForMachineAsync(segments[0], segments[1], appId))
            {
                throw new SecurityTokenException($"The application {appId} doesn't has access to the project {accessPointKey}.");
            }

            await MachineCredentialPolicy.EnsureAllowedAsync(@this.FunctionContext, segments[0], segments[1]);
            return;
        }

        var accountId = @this.GetIdentityUniqueId();
        var accessRole = await accessService.GetAccountRoleAsync(accountId, accessPointKey);
        EnsureRole(accessRole, minimalRole, accessPointKey);
#endif
    }

    // An authenticated user without the role gets 403, never 401, so clients do not sign in again for nothing.
    public static void EnsureRole(AccountRole actualRole, AccountRole minimalRole, string accessPointKey)
    {
        if (actualRole < minimalRole)
        {
            throw new AccessDeniedException($"No {minimalRole:G} access to '{accessPointKey}'.");
        }
    }

    public static Task CheckAccessToOrganizationAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return CheckAccessToAsync(@this, accessService, organization, minimalRole);
    }

    public static Task CheckAccessToProjectAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return CheckAccessToAsync(@this, accessService, $"{organization}.{project}", minimalRole);
    }

    public static string GetIdentityUniqueName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, UniqueNameClaimTypes).Trim();
    }

    public static string GetIdentityName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, NameClaimType).Trim();
    }

    // Token claims win. The provider's resolver (AuthKit only) fills what the token lacks,
    // so a deployment without a JWT template can still register accounts.
    public static async Task<(string UserName, string Name)> GetIdentityProfileAsync(
        this HttpRequestData @this,
        IUserProfileResolver? profileResolver)
    {
        var userName = @this.GetClaim(UniqueNameClaimTypes)?.Trim();
        var name = @this.GetClaim(NameClaimType)?.Trim();

        if (profileResolver is not null && (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(name)))
        {
            var profile = await profileResolver.ResolveAsync(@this.GetIdentityUniqueId(), @this.FunctionContext.CancellationToken);
            userName = string.IsNullOrEmpty(userName) ? profile?.UserName?.Trim() ?? userName : userName;
            name = string.IsNullOrEmpty(name) ? profile?.Name?.Trim() ?? name : name;
        }

        return (
            userName ?? throw new SecurityTokenException($"Missing {UniqueNameClaimTypes[0]} claim."),
            name ?? throw new SecurityTokenException($"Missing {NameClaimType} claim."));
    }

    public static string GetIdentityUniqueId(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, "sub", ClaimTypes.NameIdentifier).Trim();
    }

    public static bool IsApplicationIdentity(this HttpRequestData @this)
    {
        return TryGetApplicationIdentity(@this, out _);
    }

    public static bool TryGetApplicationIdentity(this HttpRequestData @this, [MaybeNullWhen(false)] out string appId)
    {
        if (@this.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.MachineIdentity, out var value)
            && value is string machineId
            && !string.IsNullOrEmpty(machineId))
        {
            appId = machineId;
            return true;
        }

        appId = null;
        return false;
    }

    public static string? GetClaim(this HttpRequestData @this, params string[] claimTypes)
    {
        var claims = @this.GetClaims();

        foreach (var claimType in claimTypes)
        {
            var claim = claims.FirstOrDefault(c => c.Type == claimType);
            if (claim is not null)
            {
                return claim.Value;
            }
        }

        return null;
    }

    public static string GetRequiredClaim(this HttpRequestData @this, params string[] claimTypes)
    {
        var claims = @this.GetClaims();

        foreach (var claimType in claimTypes)
        {
            var claim = claims.FirstOrDefault(c => c.Type == claimType);
            if (claim is not null)
            {
                return claim.Value;
            }
        }

        throw new SecurityTokenException($"Missing {claimTypes[0]} claim.");
    }
}
