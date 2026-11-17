# Public Pairs Only in DbCreator Seeds

## Overview

A follow-up of [Key/Value Pairs in Data Sets of DbCreator](../2026-11-15%20Key-Value%20Pairs%20in%20Data%20Sets%20of%20DbCreator.md) (424-265), decided after the PR review (see [PR Review Findings](2026-11-17%20PR%20Review%20Findings%20of%20Key-Value%20Pairs%20in%20Data%20Sets.md), section 4): the switch `--public-pairs-only` of `2sInitDb seed`, so a set with `Standard` pairs (`showcase`, `exhibit`) seeds without a Standard vault. It is one commit, `feat(platform): seed the public pairs only with --public-pairs-only (424-265)`.

## What Was Done

1. **`DataSetSeedOptions.PublicPairsOnly`**, set by `SeedCommand`'s `--public-pairs-only` (`IsPublicPairsOnly`).
2. **The plan.** `DataSetKeyValueSeeder.EnsureSeedable(set, pairs, publicPairsOnly)` does not refuse `Standard` pairs with the switch; the refusal without it names the switch. The refusals of `Hsm`, `Byok`, `Hyok` and of an item without a value stay: they are refusals of the file.
3. **The seed.** `SeedAsync` takes `publicPairsOnly`: every pair of another level than `Public` is `Skipped` with the new code `PublicPairsOnly` and never sent, also with a vault, so the switch means the same with and without one; only the `Public` pairs are cut into chunks, planned and written.
4. **The record.** A set with a pair skipped by the switch is not recorded, though nothing failed and the exit code is `0`: it is not complete, and a later seed with a vault, without `--again`, adds its `Standard` pairs and finds the rest `Unchanged`. The output says so (`Seeded in …, not recorded: its Standard pairs were skipped (--public-pairs-only); …`), and the plan prints a line about the switch.
5. **Tests.** `DataSetSeederKeyValuesTests.Seed_PublicPairsOnly_SkipsTheStandardPairs_AndASeedWithAVaultAddsThemLater`, `SeedCommandTests.Seed_PublicPairsOnly_SeedsASetWithStandardPairsWithoutAVault` (exit `2` without, `0` with the switch, no record), and the option in `CommandLineTests` and `CreateRequest`: 188 unit tests pass.
6. **Docs.** `data-sets.md`: the synopsis and the options of `seed`, the `Standard` row of the levels, and exit `2`; the exhibit's `README.md`.
