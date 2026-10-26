# Race-Free Reset of Machine Credentials

Line numbers refer to the tree this plan was written against (`origin/main` at `52f4bd9a`). No application code is changed by this document.

Linear issue [424-82](https://linear.app/424fun/issue/424-82/api-key-reset-writes-back-the-old-hash-the-new-key-fails-and-the-old). It also covers finding 1 of [424-86](https://linear.app/424fun/issue/424-86) (a reset can bring back a deleted machine). The issue has no CodeRabbit coding plan.

## Problem

Resetting a machine (`PUT v1/{organization}/{project}/access/machines/{id}`, `sform account machine reset`, **Reset** in the admin UI) must leave exactly one valid secret: the new one. The machine's other fields must stay as they were.

The first fix, commit `7b1975c3` from the 424-79 pull request, re-reads the machine after the hash store wrote the new hash. With no concurrent writer, the new key now validates and the old one is refused. Every write of the reset still replaces the **whole document** with a copy read earlier, and none of them is conditional. So the reset is still neither failure-safe nor race-free:

1. **It can bring back a deleted machine.** There are three windows:
   - A delete between the hash store's read and its upsert brings the machine back with the new hash.
   - A delete between the hash store's upsert and the re-read in `CosmosAccessService` makes the re-read return `null`. The code then falls back to the stale copy (`(current ?? machineAccess)`), so the machine comes back with the **old** hash and the old key works again (424-86).
   - A delete between `ApiKeyMachineAccessService`'s `GetAsync` and the hash store's read takes the hash store's "missing document" branch. That branch creates a partial document of kind `ApiKey`, with no annotation restriction, holding the new hash.
2. **It can revert a certificate renewal.** Renewal replaces the certificate fields under an ETag. A reset upsert landing after the renewal's write, from a copy read before it, puts back the old `CertificateThumbprint`, `NotAfter` and the other certificate fields. The renewed certificate is then refused, and the next renewal fails with "the presenting certificate does not match".
3. **Two concurrent resets both answer `200`.** Only the last hash written validates, so one caller holds a key that never works.
4. **A `Certificate` machine answers `500`.** Its reset goes to the API key service. That service finds no hash and returns `null`, which `CosmosAccessService` turns into `InvalidOperationException` ("reset failed"). The admin UI shows **Reset** for it, and the MSW mock even reissues a certificate there, which the API never does.
5. **The tests cannot see any of this.** No test resets a `CertificateAndApiKey` machine with a shared certificate, compares the stored fields before and after a reset, or deletes or renews a machine during a reset. As committed, `Access.Certificates/testint` does not run in a Testcontainers emulator: the fixture passes on the `http` connection string to an emulator that serves HTTPS.

## Current State

### The machine document

`MachineAccessEntity` (`Backend.CosmosDb/src/Entities/Access/MachineAccessEntity.cs`) has id = machine id and partition `{project}.access`, in container `org.{organization}`. It holds `AccessKey` (masked), `HashedSecret`, `Scopes`, `AnnotationKey`, `CredentialKind`, the certificate fields (`CertificateThumbprint`, `PreviousThumbprint`, `PreviousValidUntil`, `CertificateSerialNumber`, `NotBefore`, `NotAfter`, `CertificateLifetimeDays`, `IsRevoked`, `UsesSharedCertificate`, `LastRenewalAt`), `FederationRules` and `ETag` (`_etag`, filled by point reads).

Reads deserialize with System.Text.Json. Writes go through the SDK's Newtonsoft serializer with `IgnoreNullValues`, using the C# property names (`/AccessKey`, `/HashedSecret`, `/Scopes`). Cosmos patch is already in use elsewhere, with paths built from `nameof`: `CosmosConfigurationTemplateService.cs:619-623`, `ConfigurationCacheInvalidator.cs`, `CosmosAnnotationService.Deletion.cs`.

### Writers of the document

| Writer | Where | Write | Guard |
|---|---|---|---|
| Hash store `StoreAsync` | `Backend.CosmosDb/src/Accessing/CosmosMergedApiKeyHashStore.cs:25-63` | upserts the copy it read `with { HashedSecret, Scopes }`; if the read finds nothing, upserts a partial document | none |
| Hash store `DeleteAsync` | same file, 83-100 | upserts the copy it read `with { HashedSecret = null }` | none |
| Create | `CosmosAccessService.cs:520-578` | upserts the full new entity, taking `HashedSecret` from the partial document | none (new GUID) |
| Reset | `CosmosAccessService.cs:580-616` | re-reads, then upserts `(current ?? machineAccess) with { AccessKey = masked }` | none |
| Delete | `CosmosAccessService.cs:618-648` | `DeleteItemAsync`, 404 tolerated | none |
| Federation rules `PUT` | `CosmosAccessService.cs:668-710` | `ReplaceItemAsync`; on `412`, one retry, then `409` | `IfMatchEtag` |
| Certificate renewal | `CosmosCertificateRenewalService.cs:136-198` | `ReplaceItemAsync` of the certificate fields | `IfMatchEtag` |

### The reset flow

1. `AccessHttp.PutMachine` (`Api.Functions/src/V1/AccessHttp.cs:469-506`) checks the user's role, then calls `IAccessService.ResetMachineAccessAsync`. It maps only `MachineAccessNotSupportedException` to `400`. `NotFoundException` becomes `404` and `ConflictException` becomes `409` through `ErrorResponseMapping`; anything else is `500`.
2. `CosmosAccessService.ResetMachineAccessAsync` reads the document (`404` if it is missing). It then calls `PolicyAwareMachineAccessService.ResetMachineAccessAsync(MachineAccess, …)` (`Access.Certificates/src/PolicyAwareMachineAccessService.cs:116-124`), which routes on the stored kind:
   - `ClientCredentials` goes to the identity provider. The provider resets the secret there and writes nothing in Cosmos.
   - `Federated` throws `MachineAccessNotSupportedException` (`400`).
   - Every other kind, `Certificate` included, goes to `ApiKeyMachineAccessService.ResetMachineAccessAsync` (`Access.ApiKeys/src/ApiKeyMachineAccessService.cs:42-59`). That method calls `GetAsync`, returns `null` when there is no hash, generates a secret and calls `StoreAsync(…, newHash, existing.Scopes)`.
3. Back in `CosmosAccessService`, a `null` key throws `InvalidOperationException` (`500`). Otherwise it masks the key (`key[..3] + "***"`, always `2s.***` for an API key) and does the re-read and upsert. It returns the stored record with the full new key in `AccessKey`.

`ApiKeyMachineAccessService.ValidateAsync` does one point read per call and has no cache, so a stored hash takes effect on the next request.

### Clients

- `sform account machine reset` (`Cli/src/Commands/MachineAccess/MachineResetCommand.cs:38-52`) prints the response once and asks the user to copy the key. On an `ApiException` it prints the server's message. `machine list` prints the stored masked `AccessKey`.
- The admin UI (`ui.admin/src/app/pages/[org]/[project]/machines.vue:74-86, 216-221, 251-258`) shows **Reset** when `hasSecret(kind)` is true (`app/utils/machines.ts:151`: every kind but `Federated`). It shows the response once in `OneTimeSecretDialog`. The confirmation says "Existing credentials of this machine stop working at once. New ones are shown once." That is not true for the certificate of a `CertificateAndApiKey` machine.
- The MSW mock (`ui.admin/src/app/mock/access-handlers.ts:551-578`) resets through `withCredentials`. For `Certificate` and own-certificate `CertificateAndApiKey` machines, it issues a new certificate and thumbprint.

### Tests

- `Access.Certificates/testint/ScopesPersistenceTests.cs:30-52, 76-101`: after resetting an `ApiKey` machine and an own-certificate `CertificateAndApiKey` machine, the new key validates and the old one is refused.
- `CertificateLifecycleTests.cs:200-218` only checks that the returned key differs.
- `PolicyAwareScopesTests.Reset_TheApiKey_KeepsTheScopes` (`Access.Certificates/test`) uses an in-memory hash store.
- `CosmosFixture.cs:41-43` uses `DbContainer.GetConnectionString()` (`http://…`) against the HTTPS image `azure-cosmos-emulator:latest` (line 30).

## Proposed Changes

### Decisions

1. **The reset writes fields, not documents.** Every write in the reset path becomes a Cosmos patch of the fields it owns. A patch never creates a document, so a deleted machine stays deleted and the write answers `404`. A patch leaves every other field alone, so a concurrent renewal keeps its certificate fields. This is option 2 of the issue, applied to the hash store as well as to the masked key.
2. **The hash is replaced by compare-and-set.** The new hash is written only while the document still holds the hash the reset started from. That is checked against the document's ETag, as in option 3 of the issue:
   - A `412` caused by another field (a renewal) is retried after a re-read.
   - A `412` caused by a different hash (another reset) answers `409 Conflict`, so exactly one of two concurrent resets wins.
3. **A `Certificate` machine cannot be reset.** It has no secret, so its reset answers `400` with a message, like a `Federated` machine. Reissuing a certificate on reset would be a new feature with its own spec; see Out of scope.
4. **No API shape change.** The response body, the routes and the OpenAPI document stay as they are. `400` and `404` already happen on this route; `409` is new for a reset only in the concurrent case.

### 1. `IApiKeyHashStore.ReplaceAsync`

In `Backend.Core/src/Accessing/IApiKeyHashStore.cs`, add:

```csharp
// Replaces the hash of an existing machine, only while it still holds currentHashedSecret, and changes nothing else.
// False when the hash changed or was removed meanwhile; NotFoundException when the machine is gone. Never creates one.
Task<bool> ReplaceAsync(
    string organization,
    string project,
    string machineAccessId,
    string currentHashedSecret,
    string newHashedSecret);
```

`StoreAsync` keeps its meaning. It is used by create, where the machine document does not exist yet.

### 2. `CosmosMergedApiKeyHashStore`

- **`ReplaceAsync`:** at most two attempts.
  1. Point-read the document. If it is missing, throw `NotFoundException($"The machine access {id} has not been found.")`. If `HashedSecret` differs from `currentHashedSecret` (ordinal comparison, null included), return `false`.
  2. Run `PatchItemStreamAsync(id, pk, [PatchOperation.Set("/HashedSecret", newHash)], new PatchItemRequestOptions { IfMatchEtag = existing.ETag })`.
  3. On success, return `true`. On `404`, throw `NotFoundException`. On `412`, read again: the hash check decides between `false` and a second attempt. If the second attempt also gets `412`, throw `ConflictException("The machine access {id} was changed concurrently; reload and retry.")`. Any other status throws through `EnsureSuccessStatusCode`.
- **`StoreAsync`**, existing-document branch: replace the whole-document upsert with a patch of `/HashedSecret` and `/Scopes`. A `404` falls through to the partial document, as when the read found nothing. Create still works unchanged, and the store no longer writes a stale copy anywhere.
- **`DeleteAsync`:** if the read finds a hash, patch `/HashedSecret` to `null` instead of upserting the copy. A `404` means the machine is already gone and returns `true`, as today. A machine deleted in the meantime is not brought back, and a concurrent renewal is not reverted.

All patch paths are built with `nameof(MachineAccessEntity.…)`.

### 3. `ApiKeyMachineAccessService.ResetMachineAccessAsync`

1. `GetAsync`. If there is no entry, return `null` (unchanged).
2. Generate the secret and call `ReplaceAsync(org, project, objectId, existing.HashedSecret, HashSecret(secret))`.
3. If that returns `false`, throw `ConflictException($"The API key of the machine access {objectId} was reset or removed concurrently; reload and retry.")`.
4. Return the formatted key.

The scopes are no longer rewritten on a reset. They were rewritten unchanged before.

### 4. `CosmosAccessService.ResetMachineAccessAsync`

- Keep the first read: the router needs the stored kind, and a missing machine still answers `404` before any secret changes.
- Replace the re-read and the upsert with one `PatchItemStreamAsync(appId, pk, [PatchOperation.Set("/AccessKey", maskedKey)])`, with no ETag. Only the reset writes the masked key, and for API keys it is always `2s.***`.
  - On `404`, throw `NotFoundException($"The machine access {appId} was deleted during its reset.")`. Any other failure throws through `EnsureSuccessStatusCode`.
  - Otherwise deserialize the patched document from the response (System.Text.Json, as reads do). Return it `ToMachineAccess() with { AccessKey = accessKey }`.
- The stale fallback `current ?? machineAccess` goes away.

The response therefore carries the stored state after the reset, including a renewal that landed meanwhile.

### 5. A `Certificate` machine answers `400`

In `PolicyAwareMachineAccessService.ResetMachineAccessAsync(MachineAccess, …)`, add an arm:

```csharp
MachineCredentialKind.Certificate => throw new MachineAccessNotSupportedException(CertificateResetNotSupportedMessage),
```

with `public const string CertificateResetNotSupportedMessage = "A Certificate machine has no secret to reset. Its certificate is renewed in the renewal window; to replace it, create a new machine and delete this one.";`. `AccessHttp.PutMachine` already maps the exception to `400`. `sform` prints the message as it does for `Federated`.

### 6. Tests

**Integration** (`Access.Certificates/testint`, real Cosmos through the fixture): a new `MachineResetTests` class. Before the reset it snapshots the stored document; afterwards it asserts that only `HashedSecret` changed (`AccessKey` stays `2s.***` for API keys).

1. `ApiKey`, own-certificate `CertificateAndApiKey` and shared-certificate `CertificateAndApiKey`, as a theory or three facts:
   - the new key validates through `IApiKeyValidator` with the stored scopes, and the old one is refused;
   - `CredentialKind`, `Scopes`, `AnnotationKey`, the certificate fields and `UsesSharedCertificate` are unchanged;
   - for the own certificate, the certificate record is still there.
2. A `Certificate` machine: the reset throws `MachineAccessNotSupportedException`, and the document is unchanged.
3. A `ClientCredentials` machine (the fixture's recording provider): the masked `AccessKey` follows the new secret, and the other fields are unchanged.
4. A delete during the reset. Wrappers delete the document at three points:
   - Between the API key service's read and the hash replace (a wrapping `IApiKeyHashStore`): the reset throws `NotFoundException`, and no document exists afterwards.
   - After the router ran (a wrapping `IMachineAccessService`): the reset throws `NotFoundException`, no document exists, and neither key validates. This is the 424-86 window; before the fix it brings the machine back with the old hash.
   - Before the router runs: the API key service finds no hash, so the reset fails as today, without writing, and no document exists.
5. A renewal during the reset. Wrappers replace the certificate fields as a renewal does:
   - between the API key service's read and the hash replace;
   - after the router ran.

   Both keep the renewal and end with the new key validating.

   A renewal between `ReplaceAsync`'s own read and its patch, which is the `412` retry, cannot be reached from outside the store. That path follows `SetMachineFederationRulesAsync`.
6. A concurrent reset. A wrapping hash store replaces the hash first, as another reset would. The reset throws `ConflictException`, the other reset's hash stays, and the old key is refused.
7. The hash store directly:
   - `ReplaceAsync` with a stale current hash returns `false` and keeps the stored hash;
   - on a missing machine it throws `NotFoundException` and creates nothing;
   - `DeleteAsync` on a missing machine creates nothing, and on a stored machine clears only the hash.

**Unit** (`Access.Certificates/test`):

- `ApiKeyMachineAccessService` resets through `ReplaceAsync` with the stored hash. A `false` from the store becomes `ConflictException`, and a missing entry returns `null`.
- The router answers a `Certificate` reset with `MachineAccessNotSupportedException` and never calls the API key service.
- The in-memory hash store in `PolicyAwareScopesTests` gets the same compare-and-set `ReplaceAsync`.

**Fixture:** in container mode, `CosmosFixture` uses `DbContainer.GetConnectionString().Replace("http://", "https://")`. The pinned image serves HTTPS, so the suite runs as committed with `STORYTELLER_TESTS_USE_CONTAINER=true`.

### 7. Admin UI and mock

- `hasSecret` (`app/utils/machines.ts`) returns `false` for `Certificate` as well as for `Federated`. Its comment then says that a certificate machine is renewed, not reset. So a `Certificate` machine has no **Reset**. Its unit test in `test/unit/access/federation.test.ts` follows.
- The confirmation reads "The current secret of this machine stops working at once; a certificate keeps working. The new secret is shown once."
- The mock reset follows the API:
  - `Certificate` answers `400` with the API's message;
  - a `CertificateAndApiKey` reset returns only a new API key, keeping its certificate thumbprint and expiry, with no `Certificate` or `CertificatePassword`;
  - `ApiKey` and `ClientCredentials` are unchanged.

  Unit and e2e tests that expect a certificate from a reset, or **Reset** on a `Certificate` machine, follow.

### 8. Documentation

Living docs (not the specs):

- `docs/Platform/Storyteller/authentication.md`:
  - the `MachineAccess` object: a `Certificate` machine cannot be reset (`400`);
  - a reset changes only the secret, and the stored record it returns includes concurrent changes;
  - `404` when the machine is deleted during the reset, `409` when another reset won;
  - the `sform` bullet on `machine reset`.
- `docs/42for.net/platform/admin-ui.md`: no **Reset** for a certificate machine.
- `docs/42for.net/cli/sform.md`, if it describes reset beyond the role.

### Out of scope

- **Reissuing a certificate on reset**, for a `Certificate` or own-certificate `CertificateAndApiKey` machine whose private key leaked. Today the answer is to create a new machine and delete the old one.
- **Renewal writes no new `mcert.` record** (424-57 "Left for later").
- **Renewal of a document changed meanwhile:** a `412` that is not an "already renewed" rethrows the `CosmosException` as `500`.
- **Leftover partial document after a failed create:** the compensation clears the hash and keeps the document.
- **Machine list rows that are not machines:** `GetMachineAccessesAsync` runs `SELECT * FROM ma` over `{project}.access`, which also holds `mcert.*` and `scr.*` documents. This needs a check against a real project before a fix.
- **The `400`, `404` and `409` answers in the OpenAPI document** of the reset route (424-96 aligns the document).
- **424-86 finding 2:** the orphaned Entra ID application after a failed create.
