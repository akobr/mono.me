# The first Standard secret does not record the project's Standard key

Linear: [424-167](https://linear.app/424fun/issue/424-167/key-encryption-keys-card-never-shows-the-standard-key-or-rotate-now). Found by the admin UI automation run `20261102-230831-hviz` (Testiny TR-4, cases 197 and 32, local stack). The issue has no CodeRabbit coding plan. Line numbers refer to the tree this plan was written against (`main` at `05a6afcc`).

## Problem

In a new project, an Owner creates a **Standard** secret. The value is stored encrypted, and its badge shows Standard. On **Settings → Secrets**, the **Key-encryption keys** card still says "Created with the first secret, or by the daily check." for Standard. It shows no key, version or next rotation, and no **Rotate now**. Reloading for two minutes changes nothing: `GET v1/{org}/{project}/key-values/policy` never returns `Standard`.

The key itself exists. The first Standard write creates it in the vault. Nothing writes its info into the project's policy, though:

1. **The write path never records the key.** `EnvelopeValueProtector.EncryptAsync` calls `provider.WrapAsync`. `KeyVaultKeyProvider.WrapAsync` calls `EnsureKeyAsync`, which creates the key when it is missing. The returned `ProtectionKeyInfo` is dropped, and neither the protector nor `CosmosKeyValueService` saves the policy.
2. **No other path reaches a new project.** `policy.Standard` is written only by `CosmosKeyValuePolicyService.EnableAsync`, by `RotateAsync`, and by the maintenance check:
   - `EnableAsync` runs only for a level that a policy update newly allows. Standard is allowed by default, so it never qualifies.
   - **Rotate now** would record the key, but the card hides the button while the key is unknown.
   - `KeyValueProtectionTimer` runs `IKeyValueMaintenanceService.RunAsync` hourly. It visits only the projects that `CosmosKeyValuePolicyStore.ListAsync` returns, the access points with a stored `KeyValues` policy. A new project has none until an Administrator saves the policy.
3. **The daily check can skip the Standard key.** Even with a stored policy, `CheckKeysAsync` waits 20 hours after the newest `CheckedAt` of any key. An HSM key enabled today makes it skip a Standard key that is still unknown.

The local deployment is configured correctly. Aspire runs Lowkey Vault and passes `AzureKeyVaults__default` and `KeyValues__Protection__Emulator=true`. Lowkey Vault returns complete key properties (`created`, version). No Key Vault setup is missing, so the tests need no skip rule.

The failing tests:

- Case 197, `SettingsKeysRecipientsTests.SecretKeys_RotateStandardKey_ShowsNewVersion`: a disposable project, one Standard secret, then it waits for **Rotate now**. Its policy is never stored, so the key never comes.
- Case 32, `MonitorAuditTests.MonitorAudit_ProjectSecretChanges_AreRecorded`: it saves the policy first (mTLS off), so the hourly timer would record the key, but only by chance within the 75 s the test waits.

## Current State

- `Backend.Core/src/KeyValues/Protection/EnvelopeValueProtector.cs:155-207`: `EncryptAsync` reads the policy (`:161`), wraps with `provider.WrapAsync` (`:180`) and returns the envelope. It never saves the policy.
- `KeyValues.Azure.KeyVault/src/KeyVaultKeyProvider.cs`:
  - `EnsureKeyAsync` (`:97-126`) creates the key when it is missing and caches the first `ProtectionKeyInfo` per vault and key for the life of the process. After a rotation that info is stale.
  - `WrapAsync` (`:128-140`) ensures the key first.
  - `GetKeyInfoAsync` (`:168-175`) reads the key fresh.
  - `ToInfo` (`:212-223`) fills `KeyId` (versionless), `CurrentKeyVersion`, `NextRotationAt` (created + `RotationInterval`) and `CheckedAt`.
- `Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs`. Four paths write a Standard value, and none records the key:
  - `SetAsync` (`:126-215`) reads the policy once (`:134`). The create branch protects at `:161` and records the write at `:179`. The update branch protects at `:204` and records at `:209`.
  - `ProtectAsync`, the bulk re-protect (`:264-273`). `ProtectOneAsync` protects at `:1212`.
  - `ImportAsync` (`:443-539`) protects up to 8 items in parallel in `PrepareImportWritesAsync` (`:1443`). Its fallback `ImportOneAsync` (`:1478-1483`) calls `SetAsync`.
- `Backend.CosmosDb/src/KeyValues/Policy/CosmosKeyValuePolicyService.cs`:
  - `WithKeyInfo` (`:308-316`) is the one helper that sets `Standard` or `Hsm.Key`.
  - `EnableAsync` (`:413-450`) and `RotateAsync` (`:168-194`) call it.
- `Backend.CosmosDb/src/KeyValues/Policy/CosmosKeyValuePolicyStore.cs`:
  - `GetAsync` (`:36-48`) returns the defaults (`Version` 0) when nothing is stored, and caches for 60 s.
  - `SetAsync` (`:50-92`) requires `policy.Version == stored + 1`, writes with the access point's ETag, and answers `409 PolicyVersionMismatch` on a conflict, after dropping the cache. It answers `404` when the project has no access point.
  - `ListAsync` (`:94-123`) returns only the access points with `KeyValues` defined.
- `Backend.CosmosDb/src/KeyValues/Maintenance/CosmosKeyValueMaintenanceService.cs`:
  - `RunAsync` (`:52-70`) loops over `ListAsync`.
  - `CheckKeysAsync` (`:72-147`) skips when the newest `CheckedAt` is less than 20 h old (`:76-81`). It ensures the Standard key when it is unknown (`:101-104`).
  - `SaveAsync` (`:276-297`) stamps `Modified` as `AuditActor.System` and retries a version conflict twice.
- `Backend.Core/src/KeyValues/Policy/IKeyValueMaintenanceService.cs` declares `RunAsync`, `RewrapAsync` and `CheckKeysAsync`. Only `KeyValueProtectionTimer` and the tests use it; no test double implements it.
- DI (`Backend.CosmosDb/src/EntryPoint.cs:144-149`): every key/value service is a singleton. `CosmosKeyValueService` depends on the protector, the policy store, audit and usage. The maintenance service depends on the store, the providers, the repositories and audit. Neither depends on the other, so the service can take the maintenance without a cycle.
- Admin UI:
  - The policy query (`ui.admin/src/app/queries/scope.ts:116-121`) is fresh for 60 s.
  - Every key/value write calls `invalidateKeyValues` (`app/queries/invalidation.ts:133-136`) through `refresh()`: the create and import dialogs, the bulk bar (`pages/[org]/[project]/[view]/key-values.vue:114-118, 290, 331-332`) and the detail (`components/key-values/KeyValueDetail.vue:106-110`). It invalidates the view's pairs only, not the project's policy. After the API records the key, the Settings card can still show the cached policy without it for up to a minute.
  - The mock seeds `Standard` on every project (`app/mock/key-values.ts:259-292`), so the mock and e2e runs cannot show the bug.
- Tests (`Backend.CosmosDb/test`, Cosmos emulator, fake providers registered in `Startup.cs:147-157`):
  - `KeyValueMaintenanceTests.DailyCheck_NoticesANewKeyVersion` forces `CheckKeysAsync(force: true)` before it asserts `Standard`.
  - No test checks `Standard` after the first Standard write.
  - Many tests write Standard secrets into projects without an access point (`NewProject` in `CosmosKeyValueServiceTests`, `KeyValueEndToEndTests`, `KeyValueAuditAndUsageTests`). They must keep working.
- Docs: `docs/Platform/Storyteller/key-values.md`:
  - `:43` describes the defaults when no policy is stored.
  - `:50` says `Standard` is read-only.
  - `:57` says every change writes a `PolicyChange` event.
  - `:94` covers the timer, "for every project with a stored policy".
  - `:170` says the key "is created on the first Standard write of the project" but not that the policy records it.
  - The original spec (`specs/2026-10-11 Native Key-Value Storage.md:436`) wanted the key id stored in the policy on the first write. The Phase 1b review moved that to the daily check without noting that a new project is never checked.

## Proposed Changes

### Phase 1: Storyteller

#### 1. `IKeyValueMaintenanceService.RecordStandardKeyAsync`

A new method of the maintenance service, the platform's own writer of key state:

```csharp
// Records the project's Standard key in its policy when the policy does not know it yet (the first Standard write).
// Returns the policy; a project without an access point has no policy to record into and is returned as it is.
Task<KeyValuePolicy> RecordStandardKeyAsync(string organization, string project, CancellationToken cancellationToken = default);
```

`CosmosKeyValueMaintenanceService` implements it:

- Without a Standard provider, it returns the policy unchanged.
- **Up to 3 attempts.** Each reads the policy through the store (cached). When `Standard` is set, it returns the policy.
- Otherwise it reads the key fresh with `provider.GetKeyInfoAsync`. The write just wrapped with that key, so the key exists.
  - It does not use `EnsureKeyAsync`, whose cached info is stale after a rotation in the same process.
- It saves `WithKeyInfo(policy, Standard, info with { CheckedAt = now })` with `Version + 1` and `Modified = AuditStamp.Of(AuditActor.System, now)`, as the maintenance's own saves do.
- **A version conflict** (another first write, or an Administrator) starts the next attempt. The store dropped its cache, so the attempt reads the latest policy and stops once `Standard` is set.
  - It never overwrites the latest policy's other members, unlike `SaveAsync`'s merge, which keeps its own `Hsm`, `Byok` and `Rewrap`.
  - After the third conflict it throws `ConflictException` (`PolicyVersionMismatch`).
- **A project without an access point** (`NotFoundException` from `SetAsync`) returns the unchanged policy. Only tests write into such projects.
- **No audit event.** The record is the platform's state of its own key, not a policy change, as with the daily check. The key's creation is not an event either.

#### 2. The key/value writes record the key

`CosmosKeyValueService` takes an optional `IKeyValueMaintenanceService? maintenance = null` as its last constructor parameter. DI injects the registered one; the hand-built services in the tests keep compiling. A private helper does the work:

```csharp
// The first Standard secret records the project's Standard key, so Settings shows it at once (424-167).
private async Task RecordStandardKeyAsync(string organization, string project, KeyValueProtection protection, KeyValuePolicy policy)
```

- It does nothing when the level is not `Standard`, when `policy.Standard` is already set (the policy the write read, so later writes cost nothing), or when no maintenance service is given.
- It awaits `_maintenance.RecordStandardKeyAsync`.
- It catches every failure but cancellation and logs a warning. The value is already stored, so a failed record must not fail the write. The next Standard write tries again.

Called after a successful write:

| Path | When |
| --- | --- |
| `SetAsync`, create | after `RecordWriteAsync`, with the written level |
| `SetAsync`, update | after `RecordWriteAsync`, with the target level |
| `ProtectAsync` (bulk) | once after `RunBulkAsync`, when the target is `Standard` and an item is `Done` |
| `ImportAsync` | once after `ExecuteImportAsync`, when an item with `Protection == Standard` was created or updated without an error |

The import's fallback calls `SetAsync`, which records the key itself; the extra call then finds `Standard` set and does nothing. A burst of first writes costs at most one vault read and one save per writer that started before the first save. Every later attempt sees the recorded key.

`Hsm` needs nothing. The level can only be allowed through `EnableAsync`, which records the HSM key. `Byok` keeps its own state (`VerifyByokAsync` and the daily check).

#### 3. The daily check records a missing Standard key at once

`CheckKeysAsync` no longer skips a project whose Standard key is unknown while the level is allowed and has a provider:

```csharp
var standardMissing = policy.Standard is null
    && policy.AllowedProtections.Contains(KeyValueProtection.Standard)
    && _providers.ContainsKey(KeyValueProtection.Standard);

if (!force && !standardMissing && lastCheck is not null && now - lastCheck.Value < CheckInterval)
```

A stored policy with an HSM or BYOK check in the last 20 hours gets its Standard key at the next hourly run. The check then covers the other keys too, as it does without the gate.

The maintenance keeps listing only stored policies. After this change, every project gets a stored policy with its first Standard write. A project without any secret needs no key.

#### 4. Tests

In `Backend.CosmosDb/test` (Cosmos emulator, `STORYTELLER_TESTS_USE_CONTAINER=true`, the fake providers), in `KeyValuePolicyTests`:

- `FirstStandardSecret_RecordsTheStandardKey`, in a project with an access point and no stored policy:
  - Before the write: `Standard` is null and `Version` is 0.
  - After the write, the policy has:
    - `Standard.KeyId` = `https://fake.vault.local/keys/kek-standard`
    - `CurrentKeyVersion` = the fake's current version
    - `CheckedAt` set
    - `Version` 1
    - `Modified.By` = `AuditActor.System`
  - `Store.ListAsync()` (an uncached Cosmos query) now lists the project with its `Standard`, so the timer visits it.
  - A second Standard write keeps `Version` 1.
  - No `PolicyChange` event is written.
- `PublicPair_RecordsNoKey`: a public write leaves the policy at `Version` 0 with no `Standard`.
- `StandardUpdate_RecordsTheKey`: a public pair updated to `Standard` records the key (the update branch).
- `Import_OfAStandardSecret_RecordsTheKey`: an import with one public and one Standard item records the key once (`Version` 1).
- `Protect_ToStandard_RecordsTheKey`: allow `Hsm`, write an HSM secret, then `ProtectAsync` to `Standard`. The HSM key was recorded by the update, and the Standard key by the protect.
- `ConcurrentFirstStandardSecrets_RecordTheKeyOnce`: eight Standard writes at once. All succeed, and the policy ends at `Version` 1 with `Standard` set.
- `StandardSecret_WithoutAnAccessPoint_IsStoredWithoutAPolicy`: the write succeeds, the policy stays the default (`Version` 0), and `ListAsync` does not list the project.

In `KeyValueMaintenanceTests`:

- `DailyCheck_RecordsAMissingStandardKey_RightAfterAnotherCheck`: enable `Hsm`, which records the HSM key with `CheckedAt` now. A `CheckKeysAsync(force: false)` right after records `Standard`. Before the change it returned early.

In `KeyValueEndToEndTests` (Cosmos and Lowkey Vault):

- `FirstStandardSecret_RecordsTheVaultKey`: with the real `KeyVaultKeyProvider` and a maintenance service built on it, in a project with an access point:
  - The first write fills `Standard`.
  - `KeyId` ends with `/keys/kek-<hash>`.
  - `CurrentKeyVersion` is the version in the envelope's key id.
  - `NextRotationAt` is more than 300 days ahead.

#### 5. Documentation

`docs/Platform/Storyteller/key-values.md`:

- **`:43` (the defaults):** the first Standard write also stores the policy with the recorded key.
- **`:50` (`Standard`, `Hsm.Key`):** they are read-only and recorded by the platform:
  - `Standard`: by the first Standard write (create, update, protect, import), a rotation, or the daily check.
  - `Hsm.Key`: when the level is enabled, or by a rotation.
  - Both carry `CheckedAt`.
- **`:57` (audit):** every change by an Administrator writes a `PolicyChange` event. The platform's own records of its keys (the first Standard write, the daily check) write none. They do raise `Version`, so a client holding the old version gets `409 PolicyVersionMismatch` and reloads.
- **`:94` (the timer):** the check records a missing Standard key at the next run, whatever the last check of the other keys. A project gets a stored policy with its first Standard secret.
- **`:170` (the Standard level):** the first Standard write creates the key and records it in the policy (`KeyId`, `CurrentKeyVersion`, `NextRotationAt`, `CheckedAt`), so the Settings card shows it with **Rotate now**.

Code comments: `IKeyValuePolicyStore.ListAsync`, `IKeyValueMaintenanceService` (`RunAsync` and the new method), and `KeyValuePolicy.Standard`.

Historical specs and reviews under `docs/**/specs/` stay as they are.

### Phase 2: Admin UI

#### 6. Refresh the policy after the first Standard secret

`invalidateKeyValues(client, scope)` (`app/queries/invalidation.ts`) also invalidates the project's policy (`/v1/{org}/{project}/key-values/policy`) when the cached policy has no `Standard` key:

```ts
const policy = client.getQueryData<KeyValuePolicy>(['v1', scope.org, scope.project, 'key-values', 'policy'])
```

- Every write already calls this function through `refresh()`. Without a cached policy, or with the key known, nothing more is fetched.
- A write that records the key then refreshes the cached policy. The Settings card and the create dialog show the key without the 60 s wait.
- The mock keeps seeding `Standard` on every project, so the mock never takes the extra path. The mock's data is out of scope (see "Left out").

#### 7. Tests

`test/unit/key-values/key-values-invalidation.test.ts`, on a plain `QueryClient`:

- A cached policy without `Standard` is invalidated by `invalidateKeyValues`, and so are the view's pairs.
- A cached policy with `Standard` is not.
- Without a cached policy nothing fails, and only the pairs are invalidated.
- Another project's policy is never touched.

#### 8. Documentation

`docs/Platform/Storyteller/key-values.md`, "Admin UI", the **Secrets** bullet (`:423`): the Standard key appears with the project's first Standard secret, and the card says so until then.

## Phases and commits

1. `docs(platform)`: this spec.
2. `fix(platform)`, Phase 1: record the Standard key with the first Standard secret, the daily check's gate, the tests, `key-values.md` and the comments.
3. `fix(platform)`, Phase 2: refresh the admin UI's policy after a write while the key is unknown, its test, and the docs bullet.
4. `docs(platform)`: the review (`specs/reviews/2026-11-03 The first Standard secret does not record the project's Standard key.md`).

## Left out

- **A backfill.** Projects whose Standard secrets were written before this change, and whose policy was never stored, get the key with their next Standard write. An Administrator's save of the policy also lets the hourly check record it. Listing every project's access point in the maintenance would create a Standard key for projects without secrets. Finding the projects with Standard values would mean a cross-partition query of every organization each hour.
- **The UI automation tests** need no change. Their waits (`OpenUntilPolicyShowsAsync`, 75 s) already cover the API's 60 s policy cache on other instances.
- **The admin UI mock** keeps a Standard key on every project. Modelling the first write would change the seeded data, the visual baselines and the automation tests' mock runs, for no gain in the behaviour under test.
- **`CosmosMachineAuthenticationPolicyStore.SetAsync`** upserts the same access point without an ETag. A machine-policy save can therefore overwrite a concurrent `KeyValues` write. That is an existing race, not changed here.
