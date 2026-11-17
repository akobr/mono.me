# PR Review Findings of Key-Value Pairs in Data Sets

## Overview

The reviews of PR #200 by CodeRabbit and Grok on [Key/Value Pairs in Data Sets of DbCreator](../2026-11-15%20Key-Value%20Pairs%20in%20Data%20Sets%20of%20DbCreator.md) (424-265), checked against the branch. Two findings are fixed in one commit; the others are left as they are, with the reason.

## What Was Done

### 1. Fixed: a dry run of several sets did not carry the pairs (CodeRabbit)

A dry run decided each set's pairs against the store alone, so a later set's pair of a key an earlier set would create was reported `Created`, where the real run reports `Unchanged` or `Skipped`. `DataSetSeeder` now keeps `writtenPairs` (by normalized key) next to the overlay `written` in a dry run and passes it to `DataSetKeyValueSeeder.SeedAsync` (new parameter `planned`): a missing pair whose key an earlier set would create is `Unchanged` when it is equal and `Skipped` with `KeyValueExists` otherwise, and the pairs a set would create join it. `data-sets.md` drops the pairs from the remaining difference of a dry run. Test: `Seed_DryRunOfSeveralSets_DecidesAPairAgainstWhatAnEarlierSetWouldCreate`.

### 2. Fixed: a failed listing after a failed write hid the error of the write (Grok)

The listing of the active keys after a failed write could throw, and its error ended the tool instead of the write's. It is now caught (but a cancellation): every pair of the call is then `Failed` with the error of the write, and the listing's error is logged. Test: `Seed_AFailedWriteWhoseListingFailsToo_ReportsTheFailureOfTheWrite`. A comment explains why `OperationCanceledException` inside the retry aggregate counts as a vault failure (a timed-out try; the import passes no token of the seed to the vault).

### 3. Left: the race between the plan and the write (CodeRabbit, Grok)

A key created between the dry-run plan and the write is updated by the write (`Updated`, listed). A create-only write needs a new mode of the key/value import (`CosmosKeyValueService`, the API's contract), or single writes with `ExpectedVersion = 0`, which would lose the batches and still match a key deleted meanwhile. The window is the time between two calls of one seed, the docs state it, and the outcome is reported; a create-only import mode is a change of the backend beyond this issue.

### 4. Left: `showcase` and `exhibit` need a Standard vault for any seed (Grok)

The plan refuses `Standard` pairs without a vault before it reads any record, also for a set seeded already: the issue asks for the refusal before anything is written, and the plan decides from the files alone. `data-sets.md` says so, and Aspire's `db-seed` has the vault. A switch to skip `Standard` pairs is a product decision, not made here.

### 5. Left: the nits (Grok)

`key-values.json` is read twice per set (by the plan and the seed, as `annotations.jsonl` is counted and read), the result records keep their tuples (they are not serialized), `DataSetSeedItem.Kind` stays optional, the showcase's demo values stay as the mock has them, and the emulator also registers the BYOK provider, which a set never uses.

### Checks

186 unit tests of `DbCreator/test` pass; `DbCreator/testint` builds. Docker was not running, so the integration tests were not run again: the changes touch only a dry run and the failure path of a write, which the unit tests cover.
