using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;

using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;

using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.Security;

// The outcome of GetSecretRightsAsync: the caller, and per direction whether a right of the machine was withheld
// because the request has no mTLS. A refusal names mTLS only for a right the machine has, where mTLS would help.
public sealed record SecretRights(KeyValueCaller Caller, bool IsReadWithheldByMtls, bool IsWriteWithheldByMtls)
{
    public const string WithheldHeader = "X-2S-Secrets-Withheld";

    public const string WithheldByMtls = "mtls-required";

    public bool CanReadSecrets => Caller.CanReadSecrets;

    public bool CanWriteSecrets => Caller.CanWriteSecrets;

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
    // Debug builds (DEV_AUTH) skip only the checks of users: User.Impersonation and the role. Machines are checked in
    // every build. Tests of those checks look at this.
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

    // The scopes the credentials carry: scp, roles and the scope claims, without an App. prefix. For a machine they are
    // the carried half of every scope check; for a user only User.Impersonation counts. Not skipped in Debug builds.
    public static IReadOnlySet<string> GetScopes(this HttpRequestData @this)
    {
        return @this.GetClaims()
            .Where(c => c.Type is "scp" or "roles" or ClaimTypes.Role || c.Type.EndsWith("/scope"))
            .SelectMany(c => c.Value.Split(' '))
            .Select(scope => scope.StartsWith("App.", StringComparison.OrdinalIgnoreCase) ? scope[4..] : scope)
            .ToHashSet();
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

    // Who the caller is for key/value secrets, and what it may do with secret values. A machine reads them with
    // Secrets.Read and writes them with Secrets.Write, each stored on the machine and carried by its credentials, in
    // every build; when the project delivers secrets only over mTLS and the request has none, both are withheld, and
    // each is reported withheld only when the machine has it. A user needs ContributorWithSecrets for both, and no scope
    // gives them; Debug builds (DEV_AUTH) skip that role check. Handlers call AsAmbient() on the result before they
    // resolve bindings.
    public static async Task<SecretRights> GetSecretRightsAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        IKeyValuePolicyStore policies,
        string organization,
        string project)
    {
        var actor = @this.GetAuditActor();
        var correlationId = @this.FunctionContext.InvocationId;

        if (@this.TryGetApplicationIdentity(out var appId))
        {
            // The machine of the access check of this request, or one point read when there was none.
            var machine = GetStoredMachine(@this) ?? await accessService.GetMachineAccessAsync(organization, project, appId);
            var granted = machine is null ? new HashSet<MachineScope>() : GetGrantedScopes(@this, machine);
            var canRead = granted.Contains(MachineScope.SecretsRead);
            var canWrite = granted.Contains(MachineScope.SecretsWrite);
            var channel = @this.FunctionContext.Items.ContainsKey(FunctionContextItemKeys.PresentingCertificateThumbprint)
                ? KeyValueChannel.MachineMtls
                : KeyValueChannel.MachineToken;
            var policy = await policies.GetAsync(organization, project);
            var withheld = policy.RequireMtlsForSecretDelivery && channel != KeyValueChannel.MachineMtls;
            var machineCaller = new KeyValueCaller(actor, canRead && !withheld, canWrite && !withheld, channel) { CorrelationId = correlationId };
            return new SecretRights(machineCaller, IsReadWithheldByMtls: canRead && withheld, IsWriteWithheldByMtls: canWrite && withheld);
        }

#if DEV_AUTH
        var canUseSecrets = true;
#else
        var canUseSecrets = false;

        // A token that does not act for a user has no account to look up; the access check refused it already.
        if (@this.GetScopes().Contains(Scopes.User.Impersonation))
        {
            var accountId = @this.GetIdentityUniqueId();
            var accessRole = await accessService.GetAccountRoleAsync(accountId, $"{organization}.{project}");
            canUseSecrets = accessRole >= AccountRole.ContributorWithSecrets;
        }
#endif

        var user = new KeyValueCaller(actor, canUseSecrets, canUseSecrets, KeyValueChannel.User) { CorrelationId = correlationId };
        return new SecretRights(user, IsReadWithheldByMtls: false, IsWriteWithheldByMtls: false);
    }

    // The scopes a machine may use in this request: stored on the machine and carried by its credentials. It reads the
    // machine of the access check (the view transfer checks its kinds with it), so it is called after one, for machines.
    public static IReadOnlySet<MachineScope> GetGrantedMachineScopes(this HttpRequestData @this)
    {
        var machine = GetStoredMachine(@this)
            ?? throw new InvalidOperationException("The scopes of a machine are known only after the access check of the machine.");
        return GetGrantedScopes(@this, machine);
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

    // An authenticated user without the role gets 403, never 401, so clients do not sign in again for nothing.
    public static void EnsureRole(AccountRole actualRole, AccountRole minimalRole, string accessPointKey)
    {
        if (actualRole < minimalRole)
        {
            throw new AccessDeniedException($"No {minimalRole:G} access to '{accessPointKey}'.");
        }
    }

    // The guard of a route for users only: a machine is refused in every build (403, whatever its credentials carry),
    // with the route's own message when it has one. Then the token must act for a user (User.Impersonation, 401),
    // which Debug builds (DEV_AUTH) skip.
    public static void EnsureUser(this HttpRequestData @this, string? machineMessage = null)
    {
        if (@this.IsApplicationIdentity())
        {
            throw new AccessDeniedException(machineMessage ?? "Only users may call this operation; machines cannot.");
        }

#if !DEV_AUTH
        EnsureActsForUser(@this);
#endif
    }

    // A route for users only, on an access point (an organization, or organization.project): EnsureUser, then the role
    // (403), which Debug builds (DEV_AUTH) skip.
    public static async Task CheckUserAccessToAsync(this HttpRequestData @this, IAccessService accessService, string accessPointKey, AccountRole minimalRole)
    {
        await CheckUserAccessAsync(@this, accessService, accessPointKey, minimalRole);
    }

    public static async Task CheckUserAccessToOrganizationAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        AccountRole minimalRole)
    {
        await CheckUserAccessAsync(@this, accessService, organization, minimalRole);
    }

    public static async Task CheckUserAccessToProjectAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole)
    {
        await CheckUserAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole);
    }

    // An operation of the whole project (lists, key/values, templates, type-level schemas, transfers). A machine
    // restricted to an annotation is refused. A machine needs every scope, in the order given: the runtime scope
    // first, then the management scope of a management operation. Users need the role, never a scope.
    public static Task CheckAccessToProjectAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole,
        MachineScope scope,
        params MachineScope[] moreScopes)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, static restriction => restriction.EnsureUnrestricted(), [scope, .. moreScopes], needsAnyScope: false);
    }

    // Project metadata without annotation data (the views, the binding catalog): every machine of the project reads it,
    // also one restricted to an annotation. A machine needs one of the scopes, any one.
    public static Task CheckAccessToProjectMetadataAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole,
        MachineScope scope,
        params MachineScope[] orScopes)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, static _ => { }, [scope, .. orScopes], needsAnyScope: true);
    }

    // An operation on one annotation and what lies below it. A machine restricted to an annotation passes for that
    // annotation and its descendants. The key is the raw text of the request, parsed the way the handler parses it
    // afterwards; a key that does not parse is outside of every restriction. A machine needs every scope, in order.
    public static Task CheckAccessToAnnotationAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        string annotationKey,
        AccountRole minimalRole,
        MachineScope scope,
        params MachineScope[] moreScopes)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, restriction => restriction.EnsureAllows([annotationKey]), [scope, .. moreScopes], needsAnyScope: false);
    }

    // An operation on several annotations (the keys of a bulk body): a restricted machine needs every key in its scope.
    // Returns the restriction (None for users) for handlers that also filter what the operation answers. A machine
    // needs every scope, in order.
    public static Task<MachineAnnotationRestriction> CheckAccessToAnnotationsAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        IEnumerable<string?> annotationKeys,
        AccountRole minimalRole,
        MachineScope scope,
        params MachineScope[] moreScopes)
    {
        return CheckAccessAsync(@this, accessService, $"{organization}.{project}", minimalRole, restriction => restriction.EnsureAllows(annotationKeys), [scope, .. moreScopes], needsAnyScope: false);
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

    // Machines: the machine document of the project (401 without it; a federated machine also gets its project and
    // kind checked), the credential policy (401), what the operation reaches against the annotation restriction of the
    // machine (403 AnnotationRestricted), and then the scopes of the operation (403 AccessDenied). Machines have no
    // role and are checked in every build, Debug (DEV_AUTH) too. Users: the checks of CheckUserAccessAsync.
    private static async Task<MachineAnnotationRestriction> CheckAccessAsync(
        HttpRequestData request,
        IAccessService accessService,
        string accessPointKey,
        AccountRole minimalRole,
        Action<MachineAnnotationRestriction> ensureReach,
        IReadOnlyList<MachineScope> scopes,
        bool needsAnyScope)
    {
        if (request.TryGetApplicationIdentity(out var appId))
        {
            var segments = accessPointKey.Split('.', StringSplitOptions.None);

            if (segments.Length < 2)
            {
                throw new SecurityTokenException("An application can never access an organization.");
            }

            // A federated machine: one point read checks the project and the kind of its token too.
            var machine = FederatedMachineAccess.IsFederated(request.FunctionContext)
                ? await FederatedMachineAccess.EnsureAllowedAsync(request, accessService, segments[0], segments[1], appId)
                : await accessService.GetMachineAccessAsync(segments[0], segments[1], appId)
                    ?? throw new SecurityTokenException($"The application {appId} doesn't has access to the project {accessPointKey}.");

            // Kept for the secret rights and the view transfer of this request, so they need no second read.
            request.FunctionContext.Items[FunctionContextItemKeys.StoredMachine] = machine;

            await MachineCredentialPolicy.EnsureAllowedAsync(request.FunctionContext, segments[0], segments[1]);
            var restriction = MachineAnnotationRestriction.From(machine.AnnotationKey);
            ensureReach(restriction);

            if (needsAnyScope)
            {
                EnsureAnyMachineScope(request, machine, scopes);
            }
            else
            {
                EnsureMachineScopes(request, machine, scopes);
            }

            return restriction;
        }

        await CheckUserAccessAsync(request, accessService, accessPointKey, minimalRole);
        return MachineAnnotationRestriction.None;
    }

    // Users: EnsureUser, then the role on the access point (403). Debug builds (DEV_AUTH) skip User.Impersonation and
    // the role, so they also serve requests without an identity. Called by async methods only, so what EnsureUser
    // throws ends in their task.
    private static Task CheckUserAccessAsync(HttpRequestData request, IAccessService accessService, string accessPointKey, AccountRole minimalRole)
    {
        request.EnsureUser();
#if DEV_AUTH
        return Task.CompletedTask;
#else
        return EnsureUserRoleAsync(request, accessService, accessPointKey, minimalRole);
#endif
    }

    private static async Task EnsureUserRoleAsync(HttpRequestData request, IAccessService accessService, string accessPointKey, AccountRole minimalRole)
    {
        var accountId = request.GetIdentityUniqueId();
        var accessRole = await accessService.GetAccountRoleAsync(accountId, accessPointKey);
        EnsureRole(accessRole, minimalRole, accessPointKey);
    }

    // A user token carries User.Impersonation; data scopes in it are ignored, the role decides.
    private static void EnsureActsForUser(HttpRequestData request)
    {
        if (!request.GetScopes().Contains(Scopes.User.Impersonation))
        {
            throw new SecurityTokenException($"The token does not act for a user: the scope {Scopes.User.Impersonation} is missing.");
        }
    }

    // The machine document is the source of truth and the credentials can only narrow it: each scope must be stored on
    // the machine, so an app role or a provider permission added outside Storyteller grants nothing, and carried by the
    // request, so a federated token that asked for a subset stays within it.
    private static void EnsureMachineScopes(HttpRequestData request, IMachineAccess machine, IReadOnlyList<MachineScope> scopes)
    {
        var carried = request.GetScopes();

        foreach (var scope in scopes)
        {
            var name = ToScopeString(scope);

            if (!machine.Scopes.Contains(scope))
            {
                throw new AccessDeniedException($"This operation needs the scope {name}, which the machine was not created with.");
            }

            if (!carried.Contains(name))
            {
                throw new AccessDeniedException($"This operation needs the scope {name}, which the credentials of the machine do not carry.");
            }
        }
    }

    // One of the scopes is enough. Without any of them stored the machine was not created for the operation; with
    // some stored but none of those carried, its credentials narrowed it out.
    private static void EnsureAnyMachineScope(HttpRequestData request, IMachineAccess machine, IReadOnlyList<MachineScope> scopes)
    {
        var carried = request.GetScopes();
        var stored = scopes.Where(scope => machine.Scopes.Contains(scope)).ToList();

        if (stored.Any(scope => carried.Contains(ToScopeString(scope))))
        {
            return;
        }

        var names = scopes.Select(ToScopeString).Distinct().ToList();
        var needs = names.Count < 2
            ? $"the scope {names[0]}"
            : $"one of the scopes {string.Join(", ", names.Take(names.Count - 1))} or {names[^1]}";

        throw new AccessDeniedException(stored.Count == 0
            ? $"This operation needs {needs}, which the machine was not created with."
            : $"This operation needs {needs}, which the credentials of the machine do not carry.");
    }

    private static string ToScopeString(MachineScope scope)
    {
        return MachineScopes.ToScopeString(scope)
            ?? throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not a machine scope.");
    }

    private static IMachineAccess? GetStoredMachine(HttpRequestData request)
    {
        return request.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.StoredMachine, out var value)
            ? value as IMachineAccess
            : null;
    }

    private static HashSet<MachineScope> GetGrantedScopes(HttpRequestData request, IMachineAccess machine)
    {
        var carried = request.GetScopes();
        return machine.Scopes
            .Where(scope => MachineScopes.ToScopeString(scope) is { } name && carried.Contains(name))
            .ToHashSet();
    }
}
