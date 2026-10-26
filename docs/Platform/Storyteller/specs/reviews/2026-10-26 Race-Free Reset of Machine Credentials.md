# Race-Free Reset of Machine Credentials

## Overview

A machine reset now writes only the fields it owns, as Cosmos patches. The API key hash is replaced by compare-and-set under the document's ETag. As a result:

- a reset can no longer write back an old hash;
- it cannot bring back a deleted machine, and a delete at any point answers `404`;
- it keeps a concurrent certificate renewal;
- of two concurrent resets exactly one wins, and the other answers `409`.

A `Certificate` machine answers `400` instead of `500`. The admin UI shows no **Reset** for it, and no longer promises that a reset reissues a certificate.

This review covers both phases of [Race-Free Reset of Machine Credentials](../2026-10-26%20Race-Free%20Reset%20of%20Machine%20Credentials.md). Linear: [424-82](https://linear.app/424fun/issue/424-82/api-key-reset-writes-back-the-old-hash-the-new-key-fails-and-the-old). It also closes finding 1 of [424-86](https://linear.app/424fun/issue/424-86).

| Commit | Content |
|---|---|
| `4dd03aa2` | The spec |
| `29fa82ba` | Phase 1: backend, tests, `CosmosFixture`, `authentication.md` |
| `e198a713` | Phase 2: admin UI, mock, `admin-ui.md` |

## What Was Done

### Decisions

All four decisions of the spec are in place:

1. The reset writes fields, not documents.
2. The hash is replaced by compare-and-set.
3. A `Certificate` machine is refused with `400`.
4. The API shape, the routes and the OpenAPI document are unchanged.

The review round added one decision. Every delete that lands during a reset answers `404`, wherever it lands (see section 4). The spec had kept the old `500` for a delete that lands before the API key service reads the hash.

### Phase 1: Backend

1. **`IApiKeyHashStore.ReplaceAsync`** (`Backend.Core/src/Accessing/IApiKeyHashStore.cs`). The method replaces the hash only while the machine still holds `currentHashedSecret`.
   - It returns `false` when another hash replaced it.
   - It throws `NotFoundException` when the machine or its hash is gone. The hash counts because a delete removes it before the document.
   - It never creates a machine.
2. **`CosmosMergedApiKeyHashStore`**:
   - **`ReplaceAsync`** reads, then patches `/HashedSecret` with `IfMatchEtag`, in at most two attempts:
     - a missing document or a missing hash throws `NotFoundException`;
     - a different hash returns `false`;
     - a patch `404` throws `NotFoundException`;
     - a `412` re-reads, and the read decides;
     - a second `412` throws `ConflictException("The machine access {id} was changed concurrently; reload and retry.")`.
   - **`StoreAsync`** patches `/HashedSecret` and `/Scopes` first. A `404` creates the partial document as before. It no longer reads first, which saves a read on create.
   - **`DeleteAsync`** patches `/HashedSecret` to `null` instead of upserting its copy. A `404` still returns `true`.
   - All paths are built with `nameof`. Every response is disposed and goes through `EnsureSuccessStatusCode`.
3. **`ApiKeyMachineAccessService.ResetMachineAccessAsync`** resets through `ReplaceAsync` with the hash it read. A `false` throws `ConflictException("The API key of the machine access {id} was reset concurrently; reload and retry.")`. The scopes are no longer rewritten.
4. **`CosmosAccessService.ResetMachineAccessAsync`**:
   - The re-read, the upsert and the `current ?? machineAccess` fallback are gone. One patch of `/AccessKey`, with no ETag, writes the masked key.
   - The patched document is the response, so a renewal that landed meanwhile is in it.
   - **From the review round:** only a delete removes a hash. So when the first read saw a hash, a `null` key from the router answers `404` "The machine access {id} was deleted during its reset." So does a patched document whose hash is gone. The second case is a delete in progress that cleared the new hash; before this check it answered `200` with a key that never works.
   - A machine that had no hash at the first read still answers `500` "reset failed", as before.
5. **`PolicyAwareMachineAccessService`** has a `Certificate` arm that throws `MachineAccessNotSupportedException(CertificateResetNotSupportedMessage)`. That gives `400` "A Certificate machine has no secret to reset. Its certificate is renewed in the renewal window; to replace it, create a new machine and delete this one."
6. **Tests.**
   - **`Access.Certificates/testint/MachineResetTests.cs`** (new, 26 tests) runs against the Cosmos emulator. Wrappers of the router, the hash store and the Cosmos `Container` place a concurrent writer at fixed points:
     - before the router;
     - before the hash replace;
     - after the router;
     - before the masked-key write;
     - inside the hash store, between its read and its patch.

     Every test compares the stored document before and after.

     | Area | Tests |
     |---|---|
     | The reset of each kind | `ApiKey`, own- and shared-certificate `CertificateAndApiKey` (a theory): only the hash changes, the new key validates, the old one is refused. `Certificate`: refused, nothing written. `ClientCredentials`: only the masked key changes. |
     | Deletes during a reset | Finished, or with only the hash cleared, at each point: `404`, no document brought back, no working key. |
     | Renewals during a reset | Before the hash replace, after the router, and before the masked-key write: the renewal is kept. |
     | A concurrent reset | `409`, and the winner's key keeps working. |
     | The hash store directly | A stale hash, a missing machine, a delete on a missing or stored machine, a store on a stored machine. The `412` retry, a hash changed in the gap, a change before every patch. The patch `404` of `ReplaceAsync` and `DeleteAsync`. |

     The tests in the store's gap pin the status of every patch: `[NotFound]`, `[PreconditionFailed]` and so on.
   - **`Access.Certificates/test/MachineResetTests.cs`** (new, 7 tests) covers the API key service and the router: the hash replace, `false` becoming a conflict, `NotFoundException` passing through, a missing hash returning `null` without a write, and `Certificate` never reaching the API key service. `PolicyAwareScopesTests`' in-memory store got the same compare-and-set `ReplaceAsync`.
   - **`CosmosFixture`** uses `GetConnectionString().Replace("http://", "https://")` in container mode. The suite now runs as committed with `STORYTELLER_TESTS_USE_CONTAINER=true`, which closes that item of the 424-57 review.
7. **`authentication.md`**:
   - The `MachineAccess` section: the `Certificate` `400`, what a reset changes, the `409` and `404` answers with their messages, and a `ClientCredentials` machine whose delete already removed its provider application.
   - The `sform machine reset` bullet.
   - The error table: the `400` row, the `404` row, and a new `409 Conflict` row.
   - The `ApiKey` cost paragraph.
   - `access.md` and `cli/sform.md` only name the role a reset needs, so they are unchanged.

### Phase 2: Admin UI

1. **`hasSecret`** (`app/utils/machines.ts`) is `false` for `Certificate` as well as `Federated`, so a `Certificate` machine has no **Reset**. Its only use is that button.
2. **The confirmation** reads "The current secret of this machine stops working at once; a certificate keeps working. The new secret is shown once."
3. **`OneTimeSecretDialog`** (from the review round) bases its hint on what it shows:
   - "a reset issues new ones" only without a certificate;
   - for a `CertificateAndApiKey` machine with its own certificate, "a reset issues a new API key but not a new certificate: to replace the certificate, create a new machine";
   - for a `Certificate` machine, "create a new machine";
   - for a shared certificate, "issue another one".
4. **The mock reset** follows the API:
   - `Certificate` answers `400` with the API's message, defined in `mock/federation.ts`;
   - `CertificateAndApiKey` returns only a new API key and keeps its thumbprint, lifetime, expiry and renewal.
5. **Tests:**
   - unit tests for `hasSecret`, the mock `400`, the `CertificateAndApiKey` mock reset, and the four dialog hints;
   - an e2e test: the seeded `Certificate` machine has no Reset, and an API key reset shows the confirmation and then the key once, without a `.pfx`;
   - one bullet in the README's e2e list.
6. **`admin-ui.md`** has a paragraph on what **Reset** replaces and why a `Certificate` machine has none.

`sform` needed no change. `machine reset` prints the server's `400`, `404` and `409` messages, as it already did for `Federated`.

### Deviations from the spec

- **Delete windows.** Every delete during a reset answers `404`, where the spec kept the old `500` for a delete before the API key service. A delete in progress answers `404` too; it is not a `409`. The conflict message therefore lost its "or removed": it is "was reset concurrently".
- **`StoreAsync`.** It patches first instead of reading first. The behaviour is the same.
- **The store's gap is reachable after all.** The spec called the window between `ReplaceAsync`'s read and its patch unreachable from outside the store. A Moq wrapper of `Container` around the real container reaches it, so the `412` retry and the patch `404` are tested.
- **The concurrent-reset test.** The other reset is a full real reset, so its key can be checked to still work.

### Review

Four reviewers looked at the change: Cosmos and concurrency, tests, spec and docs, and the UI and mock. A skeptic then checked each finding. Six findings held up; they are three distinct issues, all fixed:

1. **The docs promised `404` for every delete during a reset, but early deletes answered `500` or `409`.** Fixed in code (section 4 and the store's null-hash check), with tests for a finished delete and a delete in progress at each point.
2. **Two gaps in the tests.**
   - No test put a renewal right before the masked-key write. Added. A mutation back to "re-read and upsert" makes it fail, while `Reset_RenewedAfterTheRouter_KeepsTheRenewal` passes against that mutation.
   - No test reached the patch `404` branches. Added.
3. **The one-time dialog still promised a new certificate for an own-certificate `CertificateAndApiKey` machine and for a shared certificate.** Fixed (Phase 2, item 3).

Refuted: the mock's `ApiKey` and `ClientCredentials` resets still go through `withCredentials`. That gap is older than this change and the spec left it on purpose (see Left for later).

### Verification

- **Integration, before the fix:** `Reset_DeletedAfterTheRouter_AnswersNotFound_AndBringsNothingBack` failed against the unchanged code. That code brought the machine back with the old hash, which is 424-86.
- **Integration, after the fix:** `STORYTELLER_TESTS_USE_CONTAINER=true dotnet test src/Platform/Storyteller/Access.Certificates/testint` passed 70 of 70, twice in a row, including `MachineResetTests` with 26.
  - One earlier full run failed every test within milliseconds because the emulator in the container had not finished starting. A single test passed right after, and both following full runs passed.
- **Mutation checks:**
  - Without the `IfMatchEtag` guard, the three `412` tests fail.
  - With the masked-key write back to "re-read and upsert", the new renewal test fails, and so does `Reset_HashClearedByADeleteAfterTheRouter_…`.
- **Unit:** `dotnet test src/Platform/Storyteller/Access.Certificates/test` passed 114 of 114.
- **Builds:** `Api.Functions` builds. `Backend.CosmosDb/test` builds but was not run, because it deletes the databases of the local emulator.
- **Admin UI:**
  - `npm run lint`, `npm run build`, and `npm test` (1448 tests before the dialog fix; the access suite, 105 tests, after it) pass.
  - `npm run test:e2e` passed 368, with 8 existing skips.
  - `npm run test:visual:docker` passed 64 with the committed baselines.

### Left for later

The "Left for later" list of the [424-57 review](2026-10-20%20Split%20Machine%20Scopes%20into%20Runtime%20and%20Management%20Scopes.md) and the out-of-scope list of the [424-79 review](2026-10-21%20Replace%20Machine%20Scope%20Presets%20with%20a%20Flat%20Scope%20Set.md) change as follows:

- **API key reset:** done by this change. The 424-79 review's "fixed after all" covered only the case without a concurrent writer.
- **`Access.Certificates/testint` with Testcontainers:** done by this change.
- **Certificate renewal writes no new `mcert.` record:** still open.
- **No compensation after a failed Entra ID call** (424-86 finding 2): still open.

Found here and left alone:

- **No reissue of a certificate on reset.** A `Certificate` or own-certificate `CertificateAndApiKey` machine whose private key leaked must be recreated. This would be a feature with its own spec.
- **Renewal against a changed document.** A `412` that is not "already renewed" rethrows the `CosmosException`, so the client gets `500`.
- **A failed create leaves a partial document.** The compensation clears the hash but keeps the document.
- **The machine list may contain certificates.** `GetMachineAccessesAsync` runs `SELECT * FROM ma` over `{project}.access`, which also holds the `mcert.*` and `scr.*` documents. That looks like rows that are not machines in the list; it needs a check against a real project.
- **The OpenAPI document of the reset route** does not declare `400`, `404` or `409` (424-96).
- **Mock gaps outside the reset fix:** an `ApiKey` reset sets `LastRenewalAt`, and a `ClientCredentials` create or reset returns no client secret in `AccessKey`.
- **Two `404` messages.** A delete in progress caught at the hash replace answers "The machine access {id} has not been found." from the store, while the other points say "…was deleted during its reset." Both are documented.
