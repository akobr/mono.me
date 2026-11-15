# Review Findings of Key-Value Pairs in Data Sets

## Overview

After phases A to C of [Key/Value Pairs in Data Sets of DbCreator](../2026-11-15%20Key-Value%20Pairs%20in%20Data%20Sets%20of%20DbCreator.md) (424-265), the branch was reviewed by three independent reviewers (the correctness of the seed, the contracts and the tests, the documentation and the acceptance criteria), and every finding was checked by a skeptical verifier that tried to refute it against the code. Eight findings were confirmed, two of them the same one; one was refuted (the showcase's pairs were seeded by the last full run, though its check was lenient, which is fixed below too). This review describes the fixes, in one commit, `fix(platform): fail the pairs of an unreachable vault, report the pairs a failed write stored, and correct the vault's host in the docs (424-265)`.

## What Was Done

### 1. A vault the seed cannot reach crashed the tool

**Finding** (confirmed twice, medium). A vault that does not answer (connection refused, a name that does not resolve, a timeout) makes Azure.Core's retry policy throw an `AggregateException` of the failed tries (`Retry failed after 4 tries…`), not a `RequestFailedException`. Neither `KeyVaultKeyProvider` nor the import maps it, so it passed the `IsPairFailure` filters of the plan and the write: the tool crashed with exit `1`, before the configurations of the set, instead of failing the pairs with exit `4`, as the docs say. The plan of a seed again hits it too, when it decrypts a stored `Standard` pair to compare it.

**Fix.** `DataSetKeyValueSeeder.IsVaultRetryFailure`: an `AggregateException` whose flattened inner exceptions are all `RequestFailedException`, `AuthenticationFailedException`, `IOException` or `OperationCanceledException` (a timeout: the import passes no token to the vault) is a failure of the pairs, `KeyEncryptionUnavailable` with `The key vault failed after N tries: {the last message}`. An aggregate of anything else still ends the tool. The mapping stays in DbCreator; mapping it in `KeyVaultKeyProvider` would fix the API as well, but changes shared code beyond this issue. Test: `Seed_AVaultItCannotReach_FailsThePairs_AndTheSeedGoesOn` (the write, the plan of a seed again, and an aggregate of another exception).

### 2. A write that failed after some batches reported stored pairs as failed

**Finding** (low). The import commits the missing pairs of a call in transactional batches of 50 and encrypts per batch, so a vault failure in a later batch leaves the earlier ones stored, while the import throws without results; the seed failed every pair of the call.

**Fix.** After such a failure, `WriteAsync` lists the active keys of the view (`GetKeysAsync`, the listing of the deleted keys made general; no vault involved) and reports the pairs planned missing that are active now `Created`, the rest `Failed`. A re-plan would decrypt the stored `Standard` pairs and fail the same way, so it is not used. `data-sets.md` says it, and that such an import writes no `Import` audit event. Test: `Seed_AWriteThatFailsAfterSomeBatches_ReportsTheStoredPairsCreated` (`FakeKeyValueService.WrittenBeforeException`).

### 3. The documented vault URI had the wrong host

**Finding** (medium). The command of `data-sets.md` (Running it) and of the exhibit's `README.md` set `AzureKeyVaults__default=https://127.0.0.1:<port>/`. Aspire gives the API `https://localhost:<port>/`, Lowkey Vault puts the host of a request into the ids of its keys, a `Standard` value records the id of its key, and `KeyVaultKeyProvider.UnwrapAsync` refuses a key of another host. A seed that followed the docs would have written pairs that the API cannot decrypt, and a later seed with the right host would fail them in its plan.

**Fix.** Both commands use `https://localhost:<port>/`, with a sentence on why the URI must be the API's; `development.md` says the same.

### 4. The refusal of Standard pairs advised the emulator to the packaged tool

**Finding** (low). The message of `EnsureSeedable` advised Lowkey Vault with `KeyValues__Protection__Emulator=true`, which a Release build (the dotnet tool) refuses at startup.

**Fix.** The message names the API's Standard vault and the roles, says that a Release build signs in with a managed identity or `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET`, and that a Debug build also takes Lowkey Vault. The unit test checks the three parts.

### 5. The exhibit's command seeded the wrong database

**Finding** (low). The exhibit's `README.md` gave the seed the Aspire host's vault but not its Cosmos DB, so DbCreator would have seeded the Windows emulator of `app.config.json`. **Fix.** The command sets `ConnectionStrings__cosmos` from the dashboard too, as `data-sets.md` does, and links to it.

### 6. The tests' need of Docker for Lowkey Vault was not documented

**Finding** (low). `DbCreator/testint` starts Lowkey Vault in Docker for its `Standard` tests also with the Windows emulator. **Fix.** The Tests section of `data-sets.md` and the tools of `development.md` say so, with `LOWKEY_VAULT_URL`.

### 7. The order of the lines before the pairs was not tested

**Finding** (low). The test of the step order had no `annotations.jsonl`. **Fix.** `Seed_WritesThePairsAfterTheAnnotationsAndBeforeTheConfigurations` adds a line and checks that the bulk writer ran before the first import of the pairs (the record counts the annotation).

### 8. Also

The integration test of `showcase` checked the pairs only when the set had a `key-values.json`; it now requires the file and a `Standard` pair. After the fixes, the 184 unit tests of `DbCreator/test` pass, and the 16 integration tests of `KeyValueSeedTests` and `BuiltInSetsTests` pass again on the vNext emulator and Lowkey Vault (2 min).
