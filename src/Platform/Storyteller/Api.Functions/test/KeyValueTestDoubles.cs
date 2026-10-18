using System.Collections.Specialized;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Usage;

using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Requests of the key/value tests: a user with a project role, or a machine with roles and an optional mTLS certificate.
// A body goes as JSON, with its content type.
internal static class KeyValueRequests
{
    public const string UserScopes = "User.Impersonation Default.Read Default.ReadWrite";

    public static TestHttpRequestData User(string method = "GET", bool secretsScope = false, Action<NameValueCollection>? query = null, string? body = null)
    {
        var (context, request) = CreateRequest(method, body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("preferred_username", "ada@example.com"),
            new("name", "Ada"),
            new("scp", secretsScope ? $"{UserScopes} {Scopes.Configuration.Secrets}" : UserScopes),
        };
        query?.Invoke(request.Query);
        return request;
    }

    public static TestHttpRequestData Machine(bool mtls, string method = "GET", Action<NameValueCollection>? query = null, string? body = null, params string[] roles)
    {
        var (context, request) = CreateRequest(method, body);
        var claims = new List<Claim> { new("sub", "machine-1") };
        claims.AddRange((roles.Length == 0 ? [Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Configuration.Secrets] : roles).Select(role => new Claim("roles", role)));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";

        if (mtls)
        {
            context.Items[FunctionContextItemKeys.PresentingCertificateThumbprint] = "ABCD";
        }

        query?.Invoke(request.Query);
        return request;
    }

    private static (TestFunctionContext Context, TestHttpRequestData Request) CreateRequest(string method, string? body)
    {
        var headers = body is null ? null : FunctionTestDoubles.JsonContentType();
        return FunctionTestDoubles.CreateRequest(new ServiceCollection().BuildServiceProvider(), headers, method, body);
    }
}

internal sealed class FixedKeyValuePolicies(bool requireMtls = true, KeyValuePolicy? policy = null) : IKeyValuePolicyStore
{
    public Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
        => Task.FromResult(policy ?? new KeyValuePolicy { RequireMtlsForSecretDelivery = requireMtls });

    public Task<KeyValuePolicy> SetAsync(string organization, string project, KeyValuePolicy policy, CancellationToken cancellationToken = default)
        => Task.FromResult(policy);

    public Task<IReadOnlyList<(string Organization, string Project, KeyValuePolicy Policy)>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<(string, string, KeyValuePolicy)>>([]);
}

// Every user has the given role on every access point; every machine has access to every project, restricted to the
// given annotation when there is one.
internal sealed class RoleAccess(AccountRole role, string? machineAnnotationKey = null) : IAccessService
{
    public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => Task.FromResult(role);

    public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => Task.FromResult(true);

    public Task<Account?> GetAccountAsync(string id) => throw new NotSupportedException();

    public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotSupportedException();

    public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotSupportedException();

    public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotSupportedException();

    public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotSupportedException();

    public Task<bool> GrantPermissionAsync(Permission model) => throw new NotSupportedException();

    public Task<bool> RevokePermissionAsync(Permission model) => throw new NotSupportedException();

    public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotSupportedException();

    public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole newRole, string actorId) => throw new NotSupportedException();

    public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotSupportedException();

    public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole newRole) => throw new NotSupportedException();

    public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotSupportedException();

    public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
        => Task.FromResult<MachineAccess?>(new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = "***",
            Scope = MachineAccessScope.SecretsReadWrite,
            AnnotationKey = machineAnnotationKey,
        });

    public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotSupportedException();

    public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotSupportedException();

    public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotSupportedException();
}

// Records the calls and plays back canned pairs; refuses secrets like the real service does.
internal sealed class RecordingKeyValueService : IKeyValueService
{
    public List<string> Calls { get; } = [];

    public KeyValueCaller? LastCaller { get; private set; }

    public KeyValueListRequest? LastList { get; private set; }

    public KeyValueReadOptions? LastRead { get; private set; }

    public KeyValueDeleteOptions? LastDelete { get; private set; }

    public KeyValueImportOptions? LastImport { get; private set; }

    public KeyValueExportRequest? LastExport { get; private set; }

    public (ulong From, ulong To)? LastDiff { get; private set; }

    public KeyValue? Pair { get; set; } = Public("k", "v");

    public bool DeleteResult { get; set; } = true;

    public List<KeyValueVersion> Versions { get; set; } = [];

    public static KeyValue Public(string key, string value) => Create(key, KeyValueProtection.Public, value);

    public static KeyValue Secret(string key, string? value) => Create(key, KeyValueProtection.Standard, value);

    public Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request)
    {
        Calls.Add("list");
        LastList = request;
        return Task.FromResult(new KeyValuesResponse { Items = [], Count = 0 });
    }

    public Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller)
    {
        Calls.Add($"get:{key}");
        LastRead = options;
        LastCaller = caller;

        if (Pair is { IsSecret: true } && options.Reveal && !caller.CanReadSecrets)
        {
            throw KeyValueErrors.SecretRightsRequired(key);
        }

        return Task.FromResult(Pair is null || Pair.IsSecret && !options.Reveal ? Pair is null ? null : Pair with { Value = null } : Pair);
    }

    public Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller)
    {
        Calls.Add($"set:{key}");
        LastCaller = caller;
        var protection = write.Protection ?? KeyValueProtection.Public;

        if (protection.IsSecret() && !caller.CanWriteSecrets)
        {
            throw KeyValueErrors.SecretRightsRequired(key);
        }

        return Task.FromResult(Create(key, protection, write.Value));
    }

    public Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller)
    {
        Calls.Add($"delete:{key}");
        LastDelete = options;
        LastCaller = caller;
        return Task.FromResult(DeleteResult);
    }

    public Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
        => Bulk("restore", request, caller);

    public Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
        => Bulk("purge", request, caller);

    public Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
        => Bulk("protect", request, caller);

    public Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key)
    {
        Calls.Add($"versions:{key}");
        return Task.FromResult<IReadOnlyList<KeyValueVersion>>(Versions);
    }

    public Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller)
    {
        Calls.Add($"version:{key}:{version}");
        LastCaller = caller;
        return Task.FromResult(Pair is null ? null : Pair with { Version = version, Value = reveal || !Pair.IsSecret ? Pair.Value : null });
    }

    public Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller)
    {
        Calls.Add($"diff:{key}");
        LastDiff = (fromVersion, toVersion);
        LastCaller = caller;

        if (Pair is { IsSecret: true } && !caller.CanReadSecrets)
        {
            throw KeyValueErrors.SecretRightsRequired(key);
        }

        return Task.FromResult(new DiffResult { Stats = new DiffStats { Additions = 0, Deletions = 0, Unchanged = 0 }, Hunks = [] });
    }

    public Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller)
    {
        Calls.Add("export");
        LastExport = request;
        LastCaller = caller;
        return Task.FromResult(new KeyValueDocument { Items = [] });
    }

    public Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller)
    {
        Calls.Add("import");
        LastImport = options;
        LastCaller = caller;
        return Task.FromResult(new KeyValueImportResult { IsDryRun = options.DryRun, IsComplete = true, Items = [] });
    }

    public Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets)
        => throw new NotSupportedException();

    private static KeyValue Create(string key, KeyValueProtection protection, string? value) => new()
    {
        Key = key,
        Protection = protection,
        Value = value,
        Version = 1,
        Created = AuditStamp.Of(AuditActor.Account("ada"), DateTimeOffset.UnixEpoch),
        Modified = AuditStamp.Of(AuditActor.Account("ada"), DateTimeOffset.UnixEpoch),
    };

    private Task<KeyValueBulkResult> Bulk(string operation, KeyValueBulkRequest request, KeyValueCaller caller)
    {
        Calls.Add($"{operation}:{request.KeyPrefix ?? string.Join('|', request.Keys ?? [])}");
        LastCaller = caller;
        return Task.FromResult(new KeyValueBulkResult { Items = [] });
    }
}

internal sealed class RecordingPolicyService : IKeyValuePolicyService
{
    public List<string> Calls { get; } = [];

    public KeyValueCaller? LastCaller { get; private set; }

    public KeyValuePolicy Policy { get; set; } = new();

    public Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        Calls.Add("get");
        return Task.FromResult(Policy);
    }

    public Task<KeyValuePolicy> UpdateAsync(string organization, string project, KeyValuePolicyUpdate update, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        Calls.Add("update");
        LastCaller = caller;
        return Task.FromResult(Policy);
    }

    public ByokSetupInfo GetByokSetup(string organization, string project)
    {
        Calls.Add("byok-setup");
        return new ByokSetupInfo { ClientId = "app", ConsentUrl = "https://login.microsoftonline.com/organizations/adminconsent?client_id=app", RequiredRole = "Key Vault Crypto Service Encryption User" };
    }

    public Task<KeyValuePolicy> VerifyByokAsync(string organization, string project, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        Calls.Add("verify");
        LastCaller = caller;
        return Task.FromResult(Policy);
    }

    public Task<KeyValuePolicy> RotateAsync(string organization, string project, KeyValueProtection protection, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        Calls.Add($"rotate:{protection}");
        LastCaller = caller;
        return Task.FromResult(Policy);
    }

    public Task<ClientEncryptionRecipient> AddRecipientAsync(string organization, string project, ClientEncryptionRecipientCreate create, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        Calls.Add($"add:{create.Name}");
        LastCaller = caller;
        return Task.FromResult(Recipient(create.Id ?? "generated", create.Name));
    }

    public Task<ClientEncryptionRecipient> UpdateRecipientAsync(string organization, string project, string id, ClientEncryptionRecipientUpdate update, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{id}");
        LastCaller = caller;
        return Task.FromResult(Recipient(id, update.Name ?? id));
    }

    public Task RemoveRecipientAsync(string organization, string project, string id, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        Calls.Add($"remove:{id}");
        LastCaller = caller;
        return Task.CompletedTask;
    }

    private static ClientEncryptionRecipient Recipient(string id, string name) => new()
    {
        Id = id,
        Name = name,
        PublicKey = new RsaJsonWebKey { N = "AQ", E = "AQAB" },
        Created = AuditStamp.Of(AuditActor.Account("ada"), DateTimeOffset.UnixEpoch),
    };
}

internal sealed class RecordingAuditLog : IKeyValueAuditLog
{
    public KeyValueAuditQuery? LastQuery { get; private set; }

    public Task WriteAsync(string organization, string project, KeyValueAuditEvent entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RecordBindingRead(string organization, string project, string view, string key, KeyValueProtection protection, KeyValueCaller caller)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<KeyValueAuditResponse> QueryAsync(string organization, string project, KeyValueAuditQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(new KeyValueAuditResponse { Items = [], Count = 0 });
    }
}

internal sealed class RecordingUsageService : IKeyValueUsageService
{
    public (DateOnly From, DateOnly To)? LastRange { get; private set; }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TakeSnapshotAsync(DateOnly day, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<KeyValueUsageReport> GetReportAsync(string organization, string project, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        LastRange = (from, to);
        return Task.FromResult(new KeyValueUsageReport { From = from, To = to, Totals = new Dictionary<string, decimal>(), Days = [] });
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
