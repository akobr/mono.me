# Append-Only Access Audit Trail

Linear: [424-263](https://linear.app/424fun/issue/424-263/record-an-append-only-audit-trail-for-role-membership-and-machine).

Line numbers refer to the tree this plan was written against (`7944671ee`). No application code is changed by this document. Paths are under `src/Platform/Storyteller/` unless they start with `src/` or `docs/`.

## Problem

Changes to who can access what leave no history. A membership change overwrites `AccessMap` on the access point and on the account in place; a machine is created, reset or deleted without a trace; a project's key/value policy and machine authentication policy are replaced without a record of who changed them. Nobody can answer the questions of an incident:

- Who made this account an Owner, and when?
- Who removed a member, and from where (the admin UI, MCP, `sform`)?
- Who created or reset this machine's credential?

Configurations, annotations and key/values already carry `AuditStamp`/`AuditActor` (`docs/Platform/Storyteller/audit-stamps.md`), and the secrets have their own audit trail (`docs/Platform/Storyteller/key-values.md`, "Audit trail"). Access changes have neither. Application Insights does not replace an audit trail: it is not append-only, its retention is short, and customers cannot read it.

ISO 27001 asks for it (A.5.18 access rights, A.8.15 logging, A.8.16 monitoring), enterprise buyers ask for an access log they can read themselves, and it completes the statement that 2S enforces and records all authorization.

## Current State

### Where access changes are written

Two homes, both found by reading every writer of `AccessPointEntity` and `MachineAccessEntity`:

- **core, partition `"access"`** (one logical partition for the whole platform, default indexing, **no TTL**: `Backend.CosmosDb/src/ContainerFactory.cs:51-62` creates `core` without `ApplySettings`, so an item's `ttl` is ignored). It holds the accounts (`id` = the account id), the access points `apt.{organization}[.{project}]` with `AccessMap`, `Description`, `MachineAuthentication` and `KeyValues` (`Entities/Access/AccessPointEntity.cs`), and the invitations `inv.{id}`.
- **organizations, key `(organization, "{project}.access")`**: the machine documents (`id` = the machine id, `Entities/Access/MachineAccessEntity.cs`), their certificate records `mcert.{thumbprint}` and the shared certificates `scr.{thumbprint}`. Key `(organization, "{project}.federation")`: the trusted issuers (`id` = the issuer name). The container has `DefaultTimeToLive = -1` (`ContainerFactory.cs:149-158`).

### The writes, per operation

`Backend.CosmosDb/src/Accessing/CosmosAccessService.cs`:

- **Memberships** all go through `UpdateMembershipAsync` (797-872): it reads the access point and the account with their ETags, asks a decision lambda for `Keep`, `Set(role)` or `Remove`, and replaces both documents in one `TransactionalBatch` of core `"access"`, retrying once on 412. `GrantPermissionAsync` (321-339), `RevokePermissionAsync` (341-365), `SetMemberRoleAsync` (392-440), `RemoveMemberAsync` (442-467) and `JoinAccessPointAsync` (469-482) are its only callers. The actor is `Permission.CreatedById` or a `string actorId`, used for the authorization only.
- **`CreateAccessPointAsync`** (161-227) makes up to three separate writes: the organization's access point (192), the project's (210) and an `UpsertItemAsync` of the owner's account without an ETag (223). **`CreateAccountAsync`** (49-95) calls it and then creates the account (93): an access point can be left without its account.
- **`UpdateAccessPointAsync`** (275-319) replaces the project's access point with its ETag (the description).
- **`CreateMachineAccessAsync`** (515-574): the provider writes first (an API key's hash into a partial machine document, `CosmosMergedApiKeyHashStore.StoreAsync`; a certificate's `mcert.` record; an identity provider's application), then one `UpsertItemAsync` of the full machine (556) commits it, with a best-effort compensation on failure.
- **`ResetMachineAccessAsync`** (576-643): for the kinds with an API key, the commit is the hash patch inside `CosmosMergedApiKeyHashStore.ReplaceAsync` (73-122, `IfMatchEtag`, three tries), reached through `PolicyAwareMachineAccessService` and `ApiKeyMachineAccessService` (`Access.ApiKeys/src/ApiKeyMachineAccessService.cs:42-64`); the service then only reads the machine back (618-619), since the mask `2s.***` does not change. For `ClientCredentials` the identity provider changes the secret, and the mask is patched only when its first three characters changed (620-624). Certificate and federated machines are refused.
- **`DeleteMachineAccessAsync`** (645-675): the provider removes the credential (hash, certificates, application), then `DeleteItemAsync` (666) removes the document.
- **`SetMachineFederationRulesAsync`** (688-730): `ReplaceItemAsync` with the ETag, one retry.

Policies and trust:

- **Key/value policy**: `CosmosKeyValuePolicyStore.SetAsync` (`Backend.CosmosDb/src/KeyValues/Policy/CosmosKeyValuePolicyStore.cs:59-100`) replaces the access point with its ETag. Its callers are `CosmosKeyValuePolicyService.SaveAsync` (455-462; from `UpdateAsync`, `VerifyByokAsync`, `RotateAsync` and the three recipient operations, each with a `KeyValueCaller` carrying the actor and the correlation id) and the maintenance service with `AuditActor.System` (`KeyValues/Maintenance/CosmosKeyValueMaintenanceService.cs:184, 339`). The service writes a key/value audit event (`PolicyChange` and others) separately, afterwards, into another container.
- **Machine authentication policy**: `CosmosMachineAuthenticationPolicyStore.SetAsync` (`Backend.CosmosDb/src/Accessing/CosmosMachineAuthenticationPolicyStore.cs:53-74`) reads the access point and **upserts it without an ETag**: a membership or key/value policy change landing in between is silently lost. Its only caller is `AccessHttp.PutMachineAuthentication` (`Api.Functions/src/V1/AccessHttp.cs:651`).
- **Trusted issuers**: `CosmosFederatedIssuerStore` (`Backend.CosmosDb/src/Accessing/CosmosFederatedIssuerStore.cs`): create (106), replace with the ETag (117-122), delete (143-146); its count (165) and list (239) query every document of `{project}.federation`. Endpoints `PutFederatedIssuer`/`DeleteFederatedIssuer` (`Api.Functions/src/V1/FederationHttp.cs:150-241`), Administrator.
- **Shared certificates**: `CosmosSharedCertificateStore` creates `scr.{thumbprint}` (114) and revokes by a replace (136); `SharedCertificateService` (`Access.Certificates/src/SharedCertificateService.cs`) and `CertificatesHttp` (`Api.Functions/src/V1/CertificatesHttp.cs:158-228`), Administrator.
- **Base seeding** of DbCreator: `Data.SeedAccountAccessAsync` (`Backend.CosmosDb/src/Data.cs:15-49`) merges accounts and access points directly with ETags, bypassing the service.

### Who the caller is

- `HttpRequestData.GetAuditActor()` (`Api.Functions/src/Security/HttpRequestDataExtensions.cs:92-112`): a machine, or the account (`sub`) with the name of the token and `Via` = the actor that acts for it (a Storyteller user token's `act.sub`, such as `mcp`); System in a DEV_AUTH build without `sub`.
- The key/value audit uses `FunctionContext.InvocationId` as its correlation id (`HttpRequestDataExtensions.cs:129`), also sent to Key Vault.
- Every access write route is user-only (`EnsureUser`, `HttpRequestDataExtensions.cs:207-217`); none passes an `AuditActor` today: `AccessHttp.cs:213, 253, 306, 339, 469, 513, 553, 651`, `MembersHttp.cs:85, 108`, `FederationHttp.cs:205, 236, 295`, `CertificatesHttp.cs:190, 223`.
- The invitation acceptance (`Backend.CosmosDb/src/Accessing/CosmosInvitationService.cs:176-214, 331-344`) calls the trusted `JoinAccessPointAsync` without an actor; the actor is the invitee, who can only accept directly (a call through an actor is never email-verified, `Api.Functions/src/V1/InvitationsHttp.cs:226-231`).
- The impersonation fix (424-262) is on its own branch; nothing here depends on `act`.

### The precedent: the key/value audit

`CosmosKeyValueAuditLog` (`Backend.CosmosDb/src/KeyValues/Audit/CosmosKeyValueAuditLog.cs`): partition `{project}.audit`, ids `kva.{yyyyMMddHHmmssfff}.{guid}`, `Time` as a sortable string (`KeyValueAuditEntity.FormatTime`), ttl from `KeyValues:AuditRetention` (400 days), `ORDER BY c.Time DESC` with 100 per page and the raw Cosmos continuation token; the endpoint `GET v1/{organization}/{project}/key-values/audit` (`Api.Functions/src/V1/KeyValueUsageHttp.cs:34-71`) with the filters `view`, `key`, `operation`, `from`, `to`. The model is in `Abstractions.Annotations/src/Model/KeyValues/KeyValueAudit.cs`. A bad continuation token answers 500 there.

### Clients

- The admin UI's secret audit is the Audit tab of the project's Monitor page (`src/Platform/ui.admin/src/app/components/monitor/AuditTrail.vue`); the organization has an Overview and a Members page (`app/pages/[org]/`), and the navigation's organization section (`app/utils/navigation.ts:142-151`) has Members for Administrators. The project name `audit` is reserved (`docs/Platform/Storyteller/access.md`).
- The SDKs: NSwag (`Sdk.NSwag`) and the Orval TypeScript SDK (`sdk.typescript`); Kiota was removed (`2026-10-19 Remove the Legacy C# SDKs.md`). Both are generated from the exported OpenAPI document.

## Proposed Changes

### 1. The model

In `Abstractions.Annotations/src/Model/AccessAudit.cs` (namespace `_42.Platform.Storyteller`, next to `AuditActor` and the key/value audit; `Abstractions.Access` references nothing):

```csharp
[JsonConverter(typeof(StringEnumConverter))]
public enum AccessAuditAction
{
    MemberGranted = 0, MemberRevoked, MemberRoleChanged, MemberRemoved, MemberJoined,
    AccessPointCreated, AccessPointUpdated,
    MachineCreated, MachineReset, MachineDeleted, MachineFederationRulesChanged,
    MachineAuthenticationPolicyChanged, KeyValuePolicyChanged,
    FederatedIssuerCreated, FederatedIssuerUpdated, FederatedIssuerDeleted,
    SharedCertificateIssued, SharedCertificateRevoked,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum AccessAuditTargetKind { Account = 0, Machine, Issuer, Certificate }

// What the change was made to, with a display snapshot taken at the change (a removed member keeps a name).
public record class AccessAuditTarget { required Kind; required string Id; string? Name; }

// One record of an organization's access audit trail. Never a secret.
public record class AccessAuditEntry
{
    required string Id; required DateTimeOffset Time; required AccessAuditAction Action;
    required AuditActor Actor;            // with Via, e.g. "Ada via mcp"
    required string AccessPointKey;       // organization or organization.project
    AccessAuditTarget? Target;
    string? OldValue; string? NewValue;   // a role name, a description, or compact JSON
    string? Detail;                       // e.g. the invitation of a join
    string? CorrelationId;                // the Functions invocation id
}

public record class AccessAuditQuery { string? Project; AccessAuditAction? Action; string? Target; DateTimeOffset? From; DateTimeOffset? To; string? ContinuationToken; int PageSize = 100; }
public record class AccessAuditResponse { required IReadOnlyList<AccessAuditEntry> Items; string? ContinuationToken; int Count; }
```

Deviations from the issue's proposal: `Time` and `Actor` as in `KeyValueAuditEvent` (the other audit list) instead of `At`/`By`; one `Target` (kind, id, name) instead of `TargetAccountId`/`TargetMachineId`, because issuers and certificates are targets too and a removed member needs its name; no `Ttl` in the public model (it is storage).

### 2. The caller and the record of a change (`Backend.Core/src/Accessing/Audit/`)

```csharp
// Who changes access, and the request it came with. The API builds it from the request; DbCreator and timers use System.
public sealed record AccessCaller(AuditActor Actor) { string? CorrelationId; static AccessCaller System; }
```

`AccessAuditValues` (Backend.Core) formats the values: a role name; compact JSON (`System.Text.Json`, no indentation, enums as names); every value capped at 4,000 characters, a longer one cut and ended with `… (truncated, sha256 <16 hex>)`. What a value never contains: an API key, a client secret, a certificate or its password, a hash, the stored mask (it keeps a client secret's first three characters), a JWKS key (only its count), a recipient's public key (only its id, name and state).

`AccessAuditOptions` (`Backend.Core/src/Accessing/Audit/AccessAuditOptions.cs`): section `AccessAudit`, `RetentionDays` = 730 (two years), validated between 1 and 3650 on start, bound in `AddCosmosDbAnnotations` like `AnnotationOptions`. (`Audit` is taken by `AnnotationAuditOptions`.)

### 3. Storage

`Backend.CosmosDb/src/Entities/Access/AccessAuditEntity.cs`: `PartitionKey`, `Organization`, `id` = `aca.{yyyyMMddHHmmssfffffff}.{guid N}` (the id order is the time order), `ttl` = `RetentionDays` in seconds, `Project` (null for an organization), `AccessPointKey`, `Time` (`KeyValueAuditEntity.FormatTime`), `Action`, `Actor`, `Target`, `OldValue`, `NewValue`, `Detail`, `CorrelationId`.

**A record lives in the partition of the change it records and is written in the same `TransactionalBatch`:**

| Change | Partition of the record |
|---|---|
| memberships, access points, both policies, base seeding | core `"access"` |
| machines, shared certificates | organizations `(organization, "{project}.access")` |
| trusted issuers | organizations `(organization, "{project}.federation")` |

No partition key differs within an operation, so no outbox is needed. `AccessAuditRecords` (Backend.CosmosDb, a singleton with the options and the `TimeProvider`) builds the entity; there is no write method of its own, nothing updates or deletes a record, and the records expire by ttl only.

**Core gets TTL.** `ContainerFactory` sets `DefaultTimeToLive = -1` on `core` too (no other setting of the organizations container: core keeps its default indexing), at creation and on an existing container through `EnsureContainerSettingsAsync`, which DbCreator runs on every start. No core document has a `ttl` today, so nothing else expires. Until DbCreator runs once after the deployment, the core records do not expire (the retention only gets longer). `docs/Platform/Storyteller/annotation-lifecycle.md` ("the `core` container is not changed") and the deployment docs change accordingly.

**Size.** Core `"access"` is one logical partition (20 GB). A record is about 1 KB; 1,000 access changes a day for two years are 0.7 GB. The docs state the limit; an archive (below) is the answer at a larger scale.

**Queries that read whole partitions** must skip the records: `GetMachineAccessesAsync` (`SELECT * FROM ma`, `CosmosAccessService.cs:487`) and the issuer count and list (`CosmosFederatedIssuerStore.cs:165, 239`) get `NOT STARTSWITH(c.id, 'aca.')`. Every other query of these partitions filters by an id prefix or an id already (checked: `CosmosClientCertificateStore`, `CosmosSharedCertificateStore`, `FindMachinesUsingIssuerAsync`, the core `"access"` queries).

### 4. Recording each operation

The rule: **one record per change, in the batch of the change; a change that changes nothing records nothing** (a membership `Keep`, an unchanged policy, an unchanged description or rule set); a failed batch stores neither.

1. **Memberships.** `UpdateMembershipAsync` gets the action and the `AccessCaller`, and adds `CreateItem(record)` to its batch: `OldValue` = the role before (none for a new member), `NewValue` = the decided role (none for a removal), `Target` = the account with its name. Grant → `MemberGranted` (also when it raises a role), revoke → `MemberRevoked`, set role → `MemberRoleChanged`, remove → `MemberRemoved` (leaving is actor = target), join → `MemberJoined` with `Detail` = the invitation and its sender. The authorization of grant, revoke, set role and remove uses `caller.Actor.Id` (today `CreatedById`/`actorId`, the same value from the API); `Permission.CreatedById` stays in the public model and is ignored by the service.
2. **Access points.** `CreateAccessPointAsync` and `CreateAccountAsync` become one batch of core `"access"`: the organization's access point when new, the project's, the account (created, or replaced with its ETag), and `AccessPointCreated` per access point created (`Target` = the owner, `NewValue` = `Owner`, `Detail` = the description). Creating the first project of a new organization creates two access points, so it records two. A concurrent creation answers 409 instead of leaving half of it. `UpdateAccessPointAsync` → `AccessPointUpdated` (old and new description) in the batch of its replace.
3. **Machines.**
   - Create: the full upsert becomes a batch with `MachineCreated` (`NewValue` = `{CredentialKind, Scopes, AnnotationKey, UsesSharedCertificate, CertificateThumbprint}`); the credential is returned only after it, the compensation stays.
   - Reset: the API key's hash patch **is** the change, and it runs inside `CosmosMergedApiKeyHashStore.ReplaceAsync`, three layers down. CosmosAccessService opens an ambient `PendingAccessAudit` (internal, Backend.CosmosDb, `AsyncLocal`, the pattern of `TransactionScope` and of `KeyValueCallerContext`) with the `MachineReset` record for that machine; the hash store, when it finds a pending record for the machine it patches, patches in a batch with it and marks it written. When nothing wrote it (`ClientCredentials`: the identity provider changed the secret outside any transaction), the service patches the mask in a batch with the record, always, not only when the mask changed. Either way the new credential is returned only after its record is committed; for an identity provider the record follows its change, and a failed batch answers 5xx without handing out the secret. `NewValue` = the credential kind, never the key.
   - Delete: the document's delete becomes a batch with `MachineDeleted` (`OldValue` = as for create). The credential is removed before (as today); a failed delete leaves the machine listed and is recorded when it is retried.
   - Federation rules: the replace becomes a batch with `MachineFederationRulesChanged` (old and new rules); a 412 of the batch retries as today.
4. **Policies.**
   - `IKeyValuePolicyStore.SetAsync(…, AccessCaller caller, …)`: the store compares the **access view** of the stored and the new policy (`AllowedProtections`, `DefaultSecretProtection`, `RequireMtlsForSecretDelivery`, `Hsm.RotationInterval`, `Byok.KeyUri`, `Byok.TenantId`, and the recipients' id, name and state) and adds `KeyValuePolicyChanged` with both views to the replace when it differs. Key versions, health, verification and rewrap (the maintenance runs, `VerifyByokAsync`, `RotateAsync`) change no view and record nothing; the key/value audit keeps recording them.
   - `IMachineAuthenticationPolicyStore.SetAsync(…, AccessCaller caller)`: a replace with the ETag (one retry on 412) instead of the upsert, which fixes the lost update, in a batch with `MachineAuthenticationPolicyChanged` when the policy differs.
5. **Trusted issuers.** `IFederatedIssuerStore.SetAsync`/`DeleteAsync` take the `AccessCaller`; create, replace and delete become batches with `FederatedIssuerCreated`/`Updated`/`Deleted` (`Target` = the issuer; values: issuer, key source, JWKS URI, the number of JWKS keys, lifetime, required claims, description).
6. **Shared certificates.** `ISharedCertificateStore.StoreAsync`/`RevokeAsync` and `SharedCertificateService` take the `AccessCaller`; the create and the revoke become batches with `SharedCertificateIssued`/`Revoked` (`Target` = the thumbprint with the label; `NewValue` = label, not before, not after; never the PFX or its password).
7. **Base seeding.** `Data.SeedAccountAccessAsync` records `AccessPointCreated` or `MemberGranted`/`MemberRoleChanged` with System in the batch of each access point it creates or changes. DbCreator's data sets go through `IAccessService` with `AccessCaller.System`.

Not recorded (follow-ups): a machine renewing its own certificate over mTLS (no change of who has access), invitations (they keep their sender and status; the join records them), account registration without a project (no access).

### 5. The service interfaces

`IAccessService` (`Backend.Core/src/Accessing/IAccessService.cs`): every write takes `AccessCaller caller` as its last parameter: `CreateAccountAsync`, `CreateAccessPointAsync` (`OwnerId` stays: DbCreator's owner is not its actor), `UpdateAccessPointAsync`, `GrantPermissionAsync`, `RevokePermissionAsync`, `SetMemberRoleAsync` and `RemoveMemberAsync` (instead of `string actorId`), `JoinAccessPointAsync` (plus the invitation detail), `CreateMachineAccessAsync`, `ResetMachineAccessAsync`, `DeleteMachineAccessAsync`, `SetMachineFederationRulesAsync`. The reads stay. `IInvitationService.AcceptInvitationAsync` takes the correlation id. The test doubles and Moq setups follow (`Api.Functions/test`, `DbCreator/test`, `Access.Federation/test`).

The API passes `request.GetAccessCaller()` (`GetAuditActor()` and `FunctionContext.InvocationId`) at every call site listed in Current State.

### 6. The read API

`IAccessAuditLog.QueryAsync(organization, AccessAuditQuery, ct)` (Backend.Core), only a read, implemented by `CosmosAccessAuditLog`:

- Two sources, each `SELECT TOP @take * FROM c WHERE STARTSWITH(c.id, 'aca.') AND c.Organization = @organization [AND c.Project = @project] [AND c.Action = @action] [AND c.Target.Id = @target] [AND c.Time >= @from] [AND c.Time <= @to] [AND c.id < @before] ORDER BY c.id DESC`: core `"access"`, and the organizations container on the organization's prefix (`OrganizationContainer.QueryOptions()`, which `OrganizationQueryGuard` accepts).
- Keyset paging over both: `take` = page size + 1 from each, merged by id (newest first), the page cut at the page size, the continuation token = base64url of `1:{last id}`. No record is skipped or repeated across pages, the records arriving meanwhile come first only on a new first page. A token that does not decode to an `aca.` id answers 400 `InvalidQuery`.

`GET v1/access/points/{key}/audit` (`Api.Functions/src/V1/AccessAuditHttp.cs`, route id `GetAccessAudit`, tag `Access`), with `project`, `action`, `target`, `from`, `to` and `continuationToken`; 200 `AccessAuditResponse`:

- `{key}` = an organization: its whole trail, for its Owners and Administrators; `project` narrows it.
- `{key}` = `organization.project`: the project's records, for the project's Owners and Administrators (a `project` different from the key's is a 400).
- User-only (`CheckUserAccessToAsync(key, Administrator)`): a machine gets 403, every other role 403 (`AccessDenied`), a bad filter 400.

The route deviates from the issue's `v1/organizations/{org}/access-audit`: every access route of an organization or project is `v1/access/points/{key}/…` (members, invitations, machine authentication), no route starts with `v1/organizations`, and the key gives the project level for free. The operation joins `OperationAccess.UserOnly`, the OpenAPI test tables and `RouteDispatchTests`.

### 7. OpenAPI and the SDKs

Export the document (`OpenApiExportTests` with `STORYTELLER_OPENAPI_EXPORT`), commit it to `Sdk.NSwag` and `sdk.typescript`, regenerate `Sdk.NSwag/src/ApiSdk.g.cs` with NSwag 14.6.3 and `sdk.typescript/src/generated` with `npm run generate`, update the READMEs' file names and function lists. The operation is under the `Access` tag, so no new client or MSW entry point. `Permission.CreatedById` stays, so the CLI is unchanged.

### 8. The admin UI: an organization's Audit page

- `app/pages/[org]/audit.vue` (the name `audit` is reserved for projects, so the path cannot hide one), in the organization section of the navigation after Members, for Administrators; `useCan('manageAccess', 'org')` gates the page and its query, as `monitor.vue` does.
- A table like `AuditTrail.vue`: time, action (with a readable label), project, target, change (old → new), actor with `Via` ("Ada via mcp"), correlation; filters project, action, target, from and to; "Load more" with the continuation token; loading, error and empty states (the empty state names the retention).
- `useAccessAudit` in `app/queries/`; `invalidateAccess` and `invalidateKeyValuePolicy` also refresh the organization's audit.
- The mock API: a store of records like `app/mock/key-values.ts` (`auditOf`/`recordAudit`), a seed for `acme`, the GET handler, and a record from every mocked access write (members, invitation accept, machines, rules, machine authentication, key/value policy, issuers, certificates, access points).
- Tests: a page unit test (rows, filters, paging, no request below Administrator), the mock store, the navigation test (its check against an `audit` entry is narrowed to the project section), an e2e flow with axe, and the visual baselines regenerated in Docker (the sidebar of every desktop page of `acme` gains the entry).

### 9. Tests

Backend (`Backend.CosmosDb/test`, the emulator):

- One test per operation: the change and exactly one record with the actor (with `Via`), the target, the old and new values and the correlation id; no record for a no-op.
- A failed batch (`StorageInterceptor.BeforeBatch` on the organizations container; a core interception through a `RequestHandler` like `ReplaceInterceptor`): neither the change nor the record is stored. A record is never written alone (each write path is a batch that contains its change).
- The query: newest first, every filter, keyset paging across both sources with a small page size, a bad token, isolation between organizations, project key vs organization key.
- The core TTL setting (like `Containers_HaveItemLevelTimeToLiveEnabled`); the machine list and the issuer list and count skip the records.
- No update or delete path: a test asserts that `IAccessAuditLog` has only `QueryAsync`, and nothing but `AccessAuditRecords` creates an `AccessAuditEntity` (reflection over the assembly's types is not enough; the review checks the writes).

`Access.Certificates/testint`: the reset of an API key machine writes its record in the hash batch; a `ClientCredentials` reset with the recording provider; the shared certificate and the delete paths.

`Api.Functions/test`: every write route passes `GetAccessCaller()` (account, name, `Via`, invocation id); the read route's filters, the 403 for a machine, a Reader and a Contributor, the project/organization key rules; the OpenAPI tables.

DbCreator: the provisioning passes System; `testint` snapshots tolerate the records.

### 10. Documentation

- `docs/Platform/Storyteller/access-audit.md` (new): what is recorded, where, the guarantees (atomic with the change; the identity provider exception), retention and its option, the API, the size limit, what is never stored.
- `access.md` (members, machines, a link), `audit-stamps.md` (the records that carry `Via`), `annotation-lifecycle.md` (containers: core TTL), `key-values.md` (policy changes are also in the access audit), `authentication.md` (machines, issuers, shared certificates), `mcp.md` (`Via`), `data-sets.md` (System records), `docs/42for.net/platform/admin-ui.md`, `src/Platform/ui.admin/README.md`, the deployment docs where they describe what DbCreator sets.

### 11. Decisions

1. Two homes and a merged read, not an outbox: every change and its record share a partition, so a batch is atomic; an outbox adds a processor and latency for no gain.
2. Keyset paging on the sortable id, not a composite of Cosmos tokens: the two sources merge without skipping or repeating.
3. The ambient pending record for the API key reset: the alternative passes an audit payload through `IMachineAccessService`, `PolicyAwareMachineAccessService`, `ApiKeyMachineAccessService` and `IApiKeyHashStore` (six implementations and their tests) for one write.
4. Changes made at an identity provider cannot join a Cosmos transaction; their record is committed before the credential is handed out.
5. A no-op records nothing.
6. Project administrators read their project's records through the project key; the organization's trail needs the organization role.

### 12. Out of scope

The daily export to EU Blob Storage with an immutability policy (tamper evidence), a `sform` command, an MCP tool, Testiny cases, recording certificate renewals and invitations, and removing `Permission.CreatedById` from the public model.

### Phases

1. **Recording**: the model, the options, the storage and TTL, every write path, the interface changes with all callers, the backend tests, the docs of what is recorded.
2. **Reading**: the query, the endpoint, OpenAPI and both SDKs, the API tests, the API docs.
3. **Admin UI**: the page, the mock, the tests and the docs.

Each phase gets its review in `docs/Platform/Storyteller/specs/reviews/` (the admin UI's in `docs/Platform/ui.admin/specs/reviews/`).
