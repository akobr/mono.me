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

    // The scopes of the caller: scp, roles and the scope claims, without an App. prefix. Not skipped in Debug builds;
    // CheckScope and TryCheckScope skip the check itself (DEV_AUTH).
    public static IReadOnlySet<string> GetScopes(this HttpRequestData @this)
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

    // An operation of the whole access point. A machine restricted to an annotation is refused (403 AnnotationRestricted).
    public static Task CheckAccessToAsync(this HttpRequestData @this, IAccessService accessService, string accessPointKey, AccountRole minimalRole = AccountRole.Reader)
    {
        return CheckAccessAsync(@this, accessService, accessPointKey, minimalRole, static restriction => restriction.EnsureUnrestricted());
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

    // An operation of the whole project (lists, key/values, templates, type-level schemas, transfers). A machine
    // restricted to an annotation is refused. A management operation names its management scope, which a machine
    // needs on top of its runtime scopes (403 AccessDenied); users are not affected by it.
    public static Task CheckAccessToProjectAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole = AccountRole.Reader,
        MachineManagementScope? managementScope = null)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, static restriction => restriction.EnsureUnrestricted(), managementScope);
    }

    // Project metadata without annotation data (the views, the binding catalog): every machine of the project reads it,
    // also one restricted to an annotation.
    public static Task CheckAccessToProjectMetadataAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, static _ => { });
    }

    // An operation on one annotation and what lies below it. A machine restricted to an annotation passes for that
    // annotation and its descendants. The key is the raw text of the request, parsed the way the handler parses it
    // afterwards; a key that does not parse is outside of every restriction.
    public static Task CheckAccessToAnnotationAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        string annotationKey,
        AccountRole minimalRole = AccountRole.Reader,
        MachineManagementScope? managementScope = null)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, restriction => restriction.EnsureAllows([annotationKey]), managementScope);
    }

    // An operation on several annotations (the keys of a bulk body): a restricted machine needs every key in its scope.
    // Returns the restriction (None for users) for handlers that also filter what the operation answers.
    public static Task<MachineAnnotationRestriction> CheckAccessToAnnotationsAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        IEnumerable<string?> annotationKeys,
        AccountRole minimalRole = AccountRole.Reader,
        MachineManagementScope? managementScope = null)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, restriction => restriction.EnsureAllows(annotationKeys), managementScope);
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

    // Machines: the machine document of the project (401 without it), the credential policy, what the operation reaches
    // against the annotation restriction of the machine (403 AnnotationRestricted), and then the management scope of a
    // management operation (403 AccessDenied). Machine credentials are checked for real in every build, so this runs in
    // Debug (DEV_AUTH) too; Debug builds skip only the role check of users, who never need a management scope.
    private static async Task<MachineAnnotationRestriction> CheckAccessAsync(
        HttpRequestData request,
        IAccessService accessService,
        string accessPointKey,
        AccountRole minimalRole,
        Action<MachineAnnotationRestriction> ensureReach,
        MachineManagementScope? managementScope = null)
    {
        if (request.TryGetApplicationIdentity(out var appId))
        {
            var segments = accessPointKey.Split('.', StringSplitOptions.None);

            if (segments.Length < 2)
            {
                throw new SecurityTokenException("An application can never access an organization.");
            }

            // A federated machine: one point read checks the kind and the scopes of its token too.
            var machine = FederatedMachineAccess.IsFederated(request.FunctionContext)
                ? await FederatedMachineAccess.EnsureAllowedAsync(request, accessService, segments[0], segments[1], appId)
                : await accessService.GetMachineAccessAsync(segments[0], segments[1], appId)
                    ?? throw new SecurityTokenException($"The application {appId} doesn't has access to the project {accessPointKey}.");

            await MachineCredentialPolicy.EnsureAllowedAsync(request.FunctionContext, segments[0], segments[1]);
            var restriction = MachineAnnotationRestriction.From(machine.AnnotationKey);
            ensureReach(restriction);

            if (managementScope is { } required)
            {
                EnsureManagementScope(request, machine, required);
            }

            return restriction;
        }

#if !DEV_AUTH
        var accountId = request.GetIdentityUniqueId();
        var accessRole = await accessService.GetAccountRoleAsync(accountId, accessPointKey);
        EnsureRole(accessRole, minimalRole, accessPointKey);
#endif
        return MachineAnnotationRestriction.None;
    }

    // The machine document is the source of truth and the credentials can only narrow it: the scope must be stored on the
    // machine, so an app role or a provider permission added outside Storyteller grants nothing, and carried by the
    // request, so a federated token that asked for a subset stays within it.
    private static void EnsureManagementScope(HttpRequestData request, IMachineAccess machine, MachineManagementScope managementScope)
    {
        var scope = MachineAccessScopes.Get(managementScope)
            ?? throw new ArgumentOutOfRangeException(nameof(managementScope), managementScope, "Not a management scope.");

        if (machine.ManagementScopes?.Contains(managementScope) != true)
        {
            throw new AccessDeniedException($"This operation needs the management scope {scope}, which the machine was not created with.");
        }

        if (!request.GetScopes().Contains(scope))
        {
            throw new AccessDeniedException($"This operation needs the management scope {scope}, which the credentials of the machine do not carry.");
        }
    }
}
