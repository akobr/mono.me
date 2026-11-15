# Phase 1 of the Access Audit Trail: Recording

Linear: [424-263](https://linear.app/424fun/issue/424-263/record-an-append-only-audit-trail-for-role-membership-and-machine).

## Overview

This review covers phase 1 of the spec [Append-Only Access Audit Trail](../2026-11-15%20Append-Only%20Access%20Audit%20Trail.md): the model, the caller, the storage and its TTL, the recording of every access change in the batch of the change, the interface changes with all their callers, the backend tests and the documentation of what is recorded (sections 1-5, 9 and 10 of the spec, without the read API of section 6). Commits: `73bed98e8` (code and tests) and `1edb1b9e3` (documentation). It was implemented as planned; the deviations are named below. The reading side (section 6 and 7) is phase 2.

## What Was Done

### 1. The model

`Abstractions.Annotations/src/Model/AccessAudit.cs`: `AccessAuditAction` (the 18 actions of the spec), `AccessAuditTargetKind`, `AccessAuditTarget` (with the factories `Account`, `Machine`, `Issuer`, `Certificate`), `AccessAuditEntry`, `AccessAuditQuery` (with `PageSize`, at most 100) and `AccessAuditResponse`, all with the Newtonsoft string enums of the key/value audit.

### 2. The caller and the values

`Backend.Core/src/Accessing/Audit/`: `AccessCaller(AuditActor Actor) { CorrelationId }` with `System` and `Account(id, name)`; `AccessAuditOptions` (`AccessAudit:RetentionDays`, 730, 1 to 3650, validated on start in `AddCosmosDbAnnotations`); `AccessAuditValues` (roles, texts, compact JSON with string enums, the 4,000-character cap with the SHA-256 marker, and the views of a machine, federation rules, the machine authentication policy, the key/value policy, an issuer and a shared certificate); `IAccessAuditLog`, the read interface of phase 2.

**Deviation:** the key/value policy's access view also holds the HSM backend (the spec named the rotation interval only), and a recipient's view a thumbprint of its public key, so a replaced key is visible without storing it.

### 3. Storage

- `Backend.CosmosDb/src/Entities/Access/AccessAuditEntity.cs` (`aca.{yyyyMMddHHmmssfffffff}.{guid}`, `ToEntry()`), `Accessing/AccessAuditRecords.cs` (a singleton with the options and the `TimeProvider`; `Default` for stores built without DI), `Accessing/AccessAuditChange.cs`, `Accessing/TransactionalBatchResponses.cs` (a failed batch as a `CosmosException` with the status of its failed operation).
- `ContainerFactory.CreateContainerIfNotExistsAsync` creates `core` with `DefaultTimeToLive = -1` and sets it on an existing one (only DbCreator calls it, on every start). Core keeps its default indexing.
- `GetMachineAccessesAsync` skips `aca.`, and also `mcert.` and `scr.`: the certificate records and shared certificates of the partition came back as machines before (with empty fields, because the client's serializer ignores `required`). The trusted issuers' count and list skip `aca.`.

### 4. Recording each operation

As planned, each in the batch of its change, and nothing for a no-op:

1. **Memberships**: `UpdateMembershipAsync` takes the action, the caller and a detail, and adds the record as the third item. Authorization uses `caller.Actor.Id`. A batch failure other than 412 is now a `CosmosException` (`ThrowIfFailed`, so a 429 or 503 keeps its status) instead of an `InvalidOperationException`.
2. **Access points**: `CreateAccessPointsAsync` (private) writes the access points, the owner's account (created, or replaced with its ETag) and a record per access point in one batch, with one retry on 409 or 412; a concurrent creation of the new account answers `AccountExists`. `CreateAccountAsync` with a project uses it, without a project it creates the account alone. `UpdateAccessPointAsync` is a batch.
3. **Machines**: create, delete and the rules as planned. The reset opens `PendingAccessAudit` (internal, `AsyncLocal`) around the provider; `CosmosMergedApiKeyHashStore.ReplaceAsync` patches the hash in a batch with the pending record when it finds one for its machine (`PatchHashWithRecordAsync`, the ETag kept on the patch) and marks it written. Otherwise (`ClientCredentials`) the service patches the mask with the record in one batch, always. `NewValue` is the kind, `Detail` names the replaced credential.
4. **Policies**: `CosmosKeyValuePolicyStore.SetAsync` records when the access view differs; the policy service passes its `KeyValueCaller`'s actor and correlation id, the maintenance service `System`. `CosmosMachineAuthenticationPolicyStore.SetAsync` replaces with the ETag and retries once, so the lost update of the upsert is gone.
5. **Trusted issuers**: create, replace and delete as batches; a delete reads the issuer first for its old value.
6. **Shared certificates**: issue and revoke as batches. **Deviation, from the tests:** the revoke replaces with the ETag of its read and reads again on 412, because two concurrent revocations both committed and both recorded (`Revoke_RacingAnotherRevoke_RecordsOneRevocation` showed it); a certificate revoked meanwhile is answered `true` without a record.
7. **Base seeding**: `Data.SeedAccountAccessAsync` takes `AccessAuditRecords` (DbCreator's `CoreDbStructureBuilder` passes the registered one) and writes each access point with its record (`AccessPointCreated`, `MemberGranted` or `MemberRoleChanged`, `Detail` "Seeded by DbCreator."); an unchanged role writes nothing (before, it replaced the document with the same content).

### 5. The service interfaces

As planned, plus: `JoinAccessPointAsync` takes the detail of the join; `IInvitationService.AcceptInvitationAsync` takes the correlation id (the API passes the invocation id), and the service builds the invitee's caller and the detail "Invitation {id} sent by {name} ({id})."; `ISharedCertificateStore`, `SharedCertificateService`, `IFederatedIssuerStore` (with `InMemoryFederatedIssuerStore`), `IKeyValuePolicyStore` and `IMachineAuthenticationPolicyStore` take the caller. `HttpRequestDataExtensions.GetAccessCaller()` is `GetAuditActor()` with `FunctionContext.InvocationId`. `Permission.CreatedById` stays in the public model and is not read.

### 9. Tests

- `Backend.CosmosDb/test`: `AccessAuditCoreRecordTests` (every membership action with and without a change, the invitation join, access points, account creation, descriptions, both policies, a membership changed during the policy write, failed membership and access point batches, the base seeding, the core TTL), `AccessAuditOrganizationRecordTests` (machines of the kinds `ApiKey`, `ClientCredentials`, `Federated`, the reset in the hash's batch, a failed reset batch that hands out no secret, delete, rules, failed batches, the machine list, issuers and their limit, shared certificates), `AccessAuditValuesTests`, and the helpers `StoredAccessAudit`/`ExpectedAccessAudit`. `InterceptedRepositoryProvider` now intercepts core too; the two description concurrency tests intercept the batch, and `ReplaceInterceptor` was removed with its last user. 885 tests, 6 skipped on vNext.
- `Access.Certificates`: `MachineAuditTests` (create and delete of every kind, refused resets), resets in `MachineResetTests` (API key, `CertificateAndApiKey`, two resets, a renewal and a lost race at the hash batch, `ClientCredentials`), shared certificates, rules, issuers and the policy; `SharedCertificateServiceTests` pass the caller. 128 unit and 95 integration tests.
- `Api.Functions/test`: `GetAccessCaller` and every write route passing it (`Via` through an actor for a membership, a machine and the policy), the invitation's invocation id. 3,100 tests.
- `DbCreator`: the provisioning passes `System`; `AccessAuditTests` (records of a seed, none on a second run, the base seeding, the core TTL on an existing container); the `StoredItems` snapshot includes the organization's records, so the tests that compare snapshots also prove that no record was written. `Access.Federation/test` and `KeyValues.Azure.KeyVault/testint` follow the signatures.

The Cosmos DB suites ran on the vNext Testcontainers emulator in chunks; the TTL is asserted on the stored `ttl`, not by waiting for an expiry.

### 10. Documentation

`docs/Platform/Storyteller/access-audit.md` (new: what is recorded, where, why atomic, what is never stored, retention, size), and `access.md`, `audit-stamps.md`, `annotation-lifecycle.md` (core TTL), `key-values.md`, `authentication.md`, `mcp.md`, `data-sets.md` and the production cookbook (DbCreator's TTL on `core`).

## Notes

- A database deployed before this change gets TTL on `core` only when DbCreator runs again; until then the records do not expire.
- The tests found no other production bug. One gap stays: no test proves that `CoreDbStructureBuilder` gets the registered `AccessAuditRecords` rather than the default (both have the same retention in the tests).
