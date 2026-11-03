# The first Standard secret does not record the project's Standard key

Linear: [424-167](https://linear.app/424fun/issue/424-167/key-encryption-keys-card-never-shows-the-standard-key-or-rotate-now).

## Overview

This review covers all of [The first Standard secret does not record the project's Standard key](../2026-11-03%20The%20first%20Standard%20secret%20does%20not%20record%20the%20project's%20Standard%20key.md), delivered in one branch (`fix/424-167`):

- **The first Standard secret records the key.** A project's first Standard write now records the project's Standard key-encryption key in its policy: `KeyId`, `CurrentKeyVersion`, `NextRotationAt` and `CheckedAt`. This covers a create, an update, a protect and an import. **Settings → Secrets** then shows the key with **Rotate now**.
- **The daily check no longer waits.** It records a missing Standard key at the next hourly run, whatever the last check of the other keys.
- **The admin UI refreshes its cached policy.** After a key/value write it refetches the policy while the cached one has no Standard key.
- **Added by the branch review:** the policy endpoints and every change of the policy read the stored policy past the instance's cache. On an API with several instances, the first Standard secret's version bump can then no longer turn a Settings save into `409 PolicyVersionMismatch`.

| Commit | Content |
| --- | --- |
| `12cf8943` | The spec |
| `4d39398e` | Phase 1: `RecordStandardKeyAsync`, the four write paths, the daily check's gate, the Cosmos and Lowkey Vault tests, `key-values.md` and the comments |
| `b4a94942` | Phase 2: `invalidateKeyValues` refreshes the policy while it has no Standard key, its Vitest test, and the admin UI bullet of `key-values.md` |
| `12735dd6` | From the branch review: `IKeyValuePolicyStore.GetCurrentAsync` for the policy endpoints and the policy's changes, a test with two instances, and the corrected docs claims |
| This commit | This review |

## What Was Done

### Phase 1: Storyteller

#### 1. `IKeyValueMaintenanceService.RecordStandardKeyAsync`

`Backend.Core/src/KeyValues/Policy/IKeyValueMaintenanceService.cs` declares the method as planned. `CosmosKeyValueMaintenanceService.RecordStandardKeyAsync` implements it:

- **Without a Standard provider** it returns the policy unchanged.
- **The loop.** Each attempt reads the policy and returns it when it knows `Standard`. Otherwise it reads the key fresh with `provider.GetKeyInfoAsync` and saves `WithKeyInfo(policy, Standard, info with { CheckedAt = now })` with `Version + 1` and `Modified` stamped by `AuditActor.System`.
- **A `PolicyVersionMismatch`** starts the next attempt, up to three. The third conflict throws.
- **A `NotFoundException`**, from a project without an access point, returns the unchanged policy.
- **No audit event** is written.

**Deviation:** the attempts read with `GetCurrentAsync` (section 9), not through the cache. The caller calls only when its own copy has no key, and another instance may have recorded the key meanwhile. A cached copy would spend a vault read and a failed save first.

#### 2. The key/value writes record the key

`CosmosKeyValueService` takes `IKeyValueMaintenanceService? maintenance = null` as its last constructor parameter. DI injects it; the two services the tests build by hand compile unchanged. The private `RecordStandardKeyAsync(organization, project, protection, policy)`:

- does nothing unless the level is `Standard`, the policy the write read has no `Standard`, and a maintenance service is given;
- logs a warning for any failure except cancellation, and never fails the write.

The calls, as planned:

- `SetAsync`, both branches, after `RecordWriteAsync`.
- `ProtectAsync`, once, when an item is `Done`.
- `ImportAsync`, once, after the import's audit event, when a `Standard` item was created or updated without an error.

A dry run returns before any of this. In the import, the call sits after the audit event instead of before the usage count; that placement keeps StyleCop's SA1513 quiet and audits the import first.

#### 3. The daily check records a missing Standard key at once

`CheckKeysAsync` computes `standardMissing`: no `Standard`, the level allowed, and a Standard provider registered. It skips the 20-hour gate in that case, as planned. Once the key is recorded, the gate applies again; `DailyCheck_RecordsAMissingStandardKey_RightAfterAnotherCheck` checks both halves.

#### 4. Tests

In `Backend.CosmosDb/test`, run on the Cosmos emulator with `STORYTELLER_TESTS_USE_CONTAINER=true`. `KeyValuePolicyTests`:

| Test | What it checks |
| --- | --- |
| `FirstStandardSecret_RecordsTheStandardKey` | The record itself. It reads `ListAsync` too, a query past the cache, so the record is stored and not only cached. A second Standard write, and an update of the first, keep `Version` 1. No `PolicyChange` event is written. |
| `PublicPair_RecordsNoKey` | A public write records nothing. |
| `StandardUpdate_RecordsTheKey` | The update branch, from public to Standard. |
| `Import_OfAStandardSecret_RecordsTheKeyOnce` | Also: a dry run records nothing. |
| `Protect_ToStandard_RecordsTheKey` | The HSM key recorded by enabling the level stays equal. `Version` 2. |
| `ConcurrentFirstStandardSecrets_RecordTheKeyOnce` | Eight writes at once end at `Version` 1. |
| `StandardSecret_WithoutAnAccessPoint_IsStoredWithoutAPolicy` | A project without an access point. |

`KeyValueMaintenanceTests.DailyCheck_RecordsAMissingStandardKey_RightAfterAnotherCheck` checks the gate.

`KeyValueEndToEndTests.FirstStandardSecret_RecordsTheVaultKey` uses the real `KeyVaultKeyProvider` on Lowkey Vault 7.3.112. The recorded `KeyId` ends with `/keys/kek-<hash>`, `NextRotationAt` is more than 300 days ahead, and the envelope stored in Cosmos carries the recorded `CurrentKeyVersion`.

The test name `Import_OfAStandardSecret_RecordsTheKeyOnce` differs slightly from the spec. Section 9 added a test the spec did not plan.

#### 5. Documentation

`docs/Platform/Storyteller/key-values.md`, in the places the spec named:

- **The policy section:** when a policy gets stored. The `Standard`/`Hsm.Key` row documents `CheckedAt` and every path that records either key.
- **Audit:** the platform's own updates write no `PolicyChange` event, stamp `Modified` as `System`, and raise `Version`.
- **The timer:** the gate.
- **The Standard level:** the record, the race, a failed record, and how a project whose secrets are older than this change gets its key.

Comments were updated in `IKeyValuePolicyStore.ListAsync`, `IKeyValueMaintenanceService` and `KeyValuePolicy.Standard`. The branch review corrected two of these claims; see section 9.

### Phase 2: Admin UI

#### 6. Refresh the policy after the first Standard secret

`app/queries/invalidation.ts`: `invalidateKeyValues` is now `async`. It reads the cached policy with `client.getQueryData(pathToQueryKey('/v1/{org}/{project}/key-values/policy'))`. It invalidates that path next to the view's pairs only when a cached policy has no `Standard`. Every write already calls the function through `refresh()`: the create and import dialogs, the bulk bar and the detail. No component changed.

#### 7. Tests

`test/unit/key-values/key-values-invalidation.test.ts`, on a plain `QueryClient`, has three tests:

- A cached policy without `Standard` is invalidated with the pairs; another project's policy is not touched.
- A policy that knows the key stays valid.
- Nothing fails without a cached policy.

The spec listed the other project as a fourth case; it is folded into the first test.

#### 8. Documentation

The **Secrets** bullet of the admin UI section in `key-values.md` says when the Standard and HSM keys show up, and that a key/value write refreshes the cached policy while it has no Standard key.

### 9. The review of the branch

Three reviewers looked at the diff through three lenses: backend correctness, regressions and tests, and the UI and docs. One skeptic per finding then tried to refute it.

**Confirmed and fixed:**

1. **A routing-dependent `409` after the first Standard secret.** The record raises `Version` on a data write. Before the branch, a fresh project's policy stayed at version 0 however many secrets were written.
   - Each API instance caches the policy for 60 s, and the instance that saves refreshes only its own cache. Another instance could show the old version, or reject the current one: `UpdateAsync` checked `ExpectedVersion` against the cached copy.
   - So on a multi-instance target, SET-SEC-02 (case 170, a Standard secret and then a save on Settings with no wait) could fail depending on routing. The plan's "the automation tests need no change" did not hold there.
   - **Fix (`12735dd6`), not in the spec:** `IKeyValuePolicyStore.GetCurrentAsync` reads the stored policy and refreshes the cache. A default interface member falls back to `GetAsync`, so the test doubles in `Api.Functions/test` and `KeyValues.Azure.KeyVault/testint` need no change.
   - `CosmosKeyValuePolicyStore` implements it, and its `GetAsync` uses it on a cache miss.
   - `CosmosKeyValuePolicyService` uses it in `GetAsync` (the policy and recipients endpoints) and in `UpdateAsync`, `VerifyByokAsync`, `RotateAsync` and the three recipient methods. A stale cache can therefore also no longer make a rotation rotate the vault key and then fail to save.
   - The key/value reads and writes, and the machine secret rule in `HttpRequestDataExtensions`, keep the cached copy.
   - `AnotherInstance_ShowsAndAcceptsTheRecordedPolicy` builds a second store and policy service with their own `MemoryCache`. With the cached reads put back, it fails with "The policy is at version 0, not 1".
2. **The audit sentence was wrong.** "Every change by an Administrator writes a `PolicyChange` audit event" (`key-values.md`) was the old claim, reworded but still wrong. Only `UpdateAsync` writes `PolicyChange`; rotation, BYOK verification and the recipient methods write `Rotate`, `ByokVerify` and `RecipientAdded`, `RecipientChanged` and `RecipientRemoved`. The sentence now says so.
3. **The policy table left two paths out.** It now lists the daily check for `Hsm.Key`, and an Administrator allowing the level again (`EnableAsync`) for `Standard`. The comment on `KeyValuePolicy.Standard` says the same.
4. **"Settings shows it at once" was false on several instances.** It is true now: the UI's refetch after the write goes to the policy endpoint, which reads past every cache.

**Refuted:**

- **"The record resets the shared 20-hour clock and delays the HSM and BYOK checks."** The gate change records a missing Standard key at the next hourly run, and that run checks the other keys in the same pass. The state the claim needs does not last.
- **"The concurrent test passes even with a broken retry."** That is a point about test strength, not a defect. The retry was read and is correct; the mutation runs below show the tests fail without the record.

## Validation

- **Backend.CosmosDb tests:** `STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test` on the final code: **652 passed**, 0 failed, in 2 min 41 s. That includes the 72 key/value tests and the Lowkey Vault end-to-end tests (Docker). The local emulator on `:8081` was not touched.
- **Mutation checks:**

  | Change | Result |
  | --- | --- |
  | The write path's record turned off, and the gate put back | 7 tests fail: the five record tests in `KeyValuePolicyTests`, the gate test in `KeyValueMaintenanceTests`, and the Lowkey Vault test |
  | The policy service's reads put back on the cache | `AnotherInstance_ShowsAndAcceptsTheRecordedPolicy` fails with `409` |
  | The admin UI's policy invalidation removed | The first Vitest test fails |

- **Other .NET tests:**

  | Project | Passed |
  | --- | --- |
  | `Backend.Core/test` | 1221 |
  | `Api.Functions/test` | 2743 |
  | `KeyValues.Azure.KeyVault/testint` (Lowkey Vault) | 13 |

- **Build:** `dotnet build src/Platform` has 0 errors. The changed files add no StyleCop warning; the SA1117 warnings left in them are older.
- **Admin UI** (after `npm run prepare:local` for the SDK and the UI): `npm run lint` passes, `vue-tsc --build` passes, and Vitest passes, 1481 tests in 102 files.

**Not run:** the automation cases 197 and 32 against the local stack (`UITESTS_PROFILE=dev`).
- No Aspire stack was running, and Azure Functions Core Tools (`func`) is not on this session's PATH.
- PR #163 also reported that the `dev` sign-in stops on AuthKit's "Continue with email" button. `Access/AuthKitSignIn.cs:53` still clicks a button named exactly "Continue".
- The cases' steps are covered at the service level: the first Standard secret records the key, and the policy endpoint returns it from every instance.

## Left for later

- **A backfill** for projects whose Standard secrets are older than this change and whose policy was never stored. They get the key with their next Standard write, or from the hourly check once an Administrator saves the policy. The reasons for not listing every project in the maintenance are in the spec's "Left out".
- **The admin UI mock** still seeds a Standard key on every project.
- **`CosmosMachineAuthenticationPolicyStore.SetAsync`** upserts the same access point without an ETag. A machine-policy save can overwrite a concurrent `KeyValues` write, which this branch makes a little more frequent. Unchanged here.
- **The automation cases 197, 32 and 170** should be re-run on a deployed or local target once the `dev` sign-in works again.
