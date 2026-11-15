# Phase A of Key-Value Pairs in Data Sets

## Overview

Phase A of the spec [Key/Value Pairs in Data Sets of DbCreator](../2026-11-15%20Key-Value%20Pairs%20in%20Data%20Sets%20of%20DbCreator.md) (424-265) covers sections 1 to 5: `key-values.json` and its refusals, the check of the levels before anything is written, the step of the pairs in the seed of a set through the key/value import, the results, the record and the output, and the Standard vault of the tool. It is one commit, `feat(platform): seed the key/value pairs of a data set's key-values.json (424-265)`, with the unit tests of `DbCreator/test` (182 pass). Section 5 was planned for phase B; it moved here, because the step catches the vault's exceptions (`Azure.RequestFailedException`, `Azure.Identity.AuthenticationFailedException`) and so needs the reference to `KeyValues.Azure.KeyVault` anyway.

## What Was Done

### 1. `key-values.json`

- **`DataSets/DataSetKeyValues.cs`** (new): a record of the items in the order of the file, with `Levels` (the count per level in the order of the enum, levels without pairs left out) and `Count(level)`. `Read(fileSystem, folder)` returns null without the file and throws `DataSetException` otherwise for: a file over `MaxFileBytes` (32 MiB, checked from its length before it is read); JSON that does not parse into `KeyValueDocument` (System.Text.Json, members in any case, unknown members refused, comments and trailing commas, `JsonStringEnumConverter(allowIntegerValues: false)`, so `"Protection": 1` and an unknown level are refused, and the C# `required` members `Items`, `Key` and `Protection` enforced; the reader's message names the path and position); a `Format` other than `2s.key-values.v1` (a missing one keeps the default of the record, so it counts as the current one); `Items` null; more than `MaxPairs` (10,000).
- **`Validate(items)`** checks every item and gives one problem per item, in the order of the import's rules: a null item; `KeyValueRules.EnsureKey`; the level (`SeedableLevels`: `Public`, `Standard`); a missing `Value`; `KeyValueRules.EnsureValue` and `EnsureContentType`; a reference (`KeyValueContentTypes.IsReference`) that is not `Public`, or whose shape `SecretReferences.Ensure` refuses; a key listed twice ignoring case (`first as item N`), checked only for items without another problem. The import's messages are reused where it has them (`InvalidInputException.Message`); the refusals of the seed have their own. The exception lists at most `MaxListedProblems` (20), one per line, then `… and N more`.
- **`DataSetFolder.GetEntries`** leaves `key-values.json` at the root out of the reader's entries, so it is neither a document nor an ignored file; one deeper is still an ignored file. The hash was already over every file.
- **`DataSetContent`** gets the positional member `KeyValues` (default null, so the existing constructions compile), read by `Read`; **`DataSetOverview`** gets `KeyValues` for `sets`, so the listing refuses what a seed refuses.

### 2. The levels before anything is written

- **`DataSets/DataSetKeyValueSeeder.cs`** (new, registered by `AddDbCreator`): it takes `IKeyValueService`, `IEnumerable<IKeyEncryptionProvider>` (the levels of the registered providers; `Public` needs none), `IOptions<KeyValueOptions>` (the name of the Standard vault for the message) and a logger.
- **`EnsureSeedable(set, pairs)`** throws for `Standard` pairs without a `Standard` provider, with the message of the spec (`AzureKeyVaults__default`, Lowkey Vault with `KeyValues__Protection__Emulator=true`, the two roles).
- **`DataSetSeeder.Plan`** reads the `key-values.json` of every set of the plan, the dependencies included, after the limits of its files and lines, and calls `EnsureSeedable`. The seeder's constructor takes the new seeder after the bulk writer.

### 3. Seeding the pairs

`DataSetKeyValueSeeder.SeedAsync(target, pairs, dryRun, items, cancellationToken)`, called by `SeedSetAsync` after `CompleteRelationsAsync` and the lines, before the import of the schemas, templates and configurations:

- **Chunks** of `ChunkSize` (1,000) indexes in file order; the cancellation is checked before each chunk and before each write.
- **The plan** (`PlanAsync`): `ImportAsync` with `Merge` and `DryRun`; the result is mapped back to the indexes by the normalized key. A `KeyValueImportInvalidException` fails the items it lists with their codes (`FailListed`) and plans the rest again; an exception that lists none fails the chunk. Any other failure of a pair (below) fails the chunk.
- **The decisions** as in the table of the spec: `Unchanged`; `Update` is `Skipped` with `KeyValueExists`; `Create` of a key in the set of deleted keys is `Skipped` with `KeyValueDeleted`, otherwise the index is missing. The deleted keys are listed lazily (`deleted ??=`), once per set, with `ListAsync` (`State = Deleted`) over every page. An action other than these three (a `Delete`, which `Merge` never plans) throws `InvalidOperationException`.
- **The write** (`WriteAsync`): one `ImportAsync` without `DryRun` of the missing indexes, none when nothing is missing; an item with an error code is `Failed`, `Create` is `Created`, `Unchanged` is `Unchanged`, anything else `Updated` with a message about the other writer. A `KeyValueImportInvalidException` at write time fails the listed items with their codes and the rest with the exception's.
- **Failures of a pair** (`IsPairFailure`): `StorytellerException` (its code), `KeyValueIntegrityException` (`KeyValueIntegrity`), and the vault's `RequestFailedException` and `AuthenticationFailedException` (`KeyEncryptionUnavailable`, `The key vault failed: …`). `KeyValueStorageException` is not among them, unlike in the spec: the import catches it itself in its fallback of single writes, and one that escapes is a failure of the storage, which ends the tool as a crash, as the bulk writer's do.
- **`Complete`** tallies the outcomes into a `ViewImportSummary` and adds an item per outcome but `Created` and `Unchanged`, in file order; an index without an outcome (a bug) would count as `Failed`. One log line per set.
- The codes `KeyValueExists` and `KeyValueDeleted` are constants of `DataSetKeyValueSeeder`, not of `ErrorCodes`: they are outcomes of a seed.

### 4. Results, record and output

- **`DataSetKeyValuesResult`** (new): `Pairs`, `Levels`, `Summary`. **`DataSetSeedResult.KeyValues`**; the pairs' items join `Items`, so `HasFailures`, `Failures` and `ExitCodes.Of` need no change.
- **`DataSetSeedItem`**: `Kind` is `ViewDocumentKind?` and no longer `required` (nothing read it), and `KeyValueKey` and `Protection` are new.
- **`DataSetSeedRecord.KeyValues`**; `CreateRecord` adds the pairs' `Created`, `Updated`, `Unchanged` and `Skipped`.
- **`DataSetOutput`**: the pairs line of a set (`key-values.json: 2 pairs (2 Public): 2 created, 0 updated, 0 unchanged, 0 skipped, 0 failed`, without the warnings column of the documents, as pairs have none), `No documents.` only without pairs too, a pair's place in the failures (`key-values.json (smtp.host)`), the pairs in the total of the core seed's summary, and the line of `sets` (`key-values.json: 20 pairs: 19 Public, 1 Standard (Standard pairs seed with a Standard vault only)`). The other outcomes join "Not created" by `{Outcome} {ErrorCode}` as before.
- **`ExitCodes`**: the comments of `2` and `4` name the pairs.

### 5. The vault in DbCreator

- `DbCreator.csproj` references `KeyValues.Azure.KeyVault`; `Startup.ConfigureServices` calls `AddKeyVaultKeyProtection(configuration, Environment)` between `AddCosmosDbAnnotations` and `AddDbCreator`. `AddDbCreator` documents that the providers are the host's. `sets` against the built output now prints the exhibit's line above.

### Tests (`DbCreator/test`)

- `DataSetKeyValuesTests` (new): the export's document, any case with comments and no format, twelve files that are not documents of pairs, nine items a seed refuses, the sizes at and over the limits, a duplicate ignoring case, the listing of 26 problems (20 lines and `… and 6 more`), 10,001 pairs, a file over 32 MiB.
- `DataSetSeederKeyValuesTests` (new) over `FakeKeyValueService` (new: an in-memory store with the import's plan, refusals, failing writes, a thrown write, a paged listing of deleted keys, and `OnImport`): the place among the steps and the System caller, only the missing pairs written with the five other outcomes (two pages of deleted keys), a dry run and a second run, 2,500 pairs in three chunks, a re-plan after a refusal (exit `4`, no record), a failed write, a thrown write (`RequestFailedException`, `ProtectionNotConfigured`, and a crash for anything else), a set without pairs or with an empty file, `Standard` without a vault refused before any call, and an `Hsm` pair of a dependency refused by `Plan`.
- `SeedCommandTests`: exit `2` for a `Standard`, an `Hsm`, a valueless and an invalid item, also in a dry run, before the layout check; `Public` pairs without a vault, and exit `4` for a failed pair; `ExitCodes.Of` with pairs. `SetsCommandTests`: the listing of a set with pairs without a vault, exit `2` for a `Byok` pair. `DataSetFolderTests`, `DataSetOverviewTests` and `BuiltInSetsTests` (the exhibit's 19 + 1 pairs, ordered by key) as planned. `SeedFixture` and `DataSetSeederTests` construct the seeder with the new argument.
