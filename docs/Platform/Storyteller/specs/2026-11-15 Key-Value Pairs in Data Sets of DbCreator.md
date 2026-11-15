# Key/Value Pairs in Data Sets of DbCreator

Linear: [424-265](https://linear.app/424fun/issue/424-265/dbcreator-seed-keyvalue-pairs-with-data-sets)

## Problem

The data sets of DbCreator ([Data Sets in DbCreator](2026-11-12%20Data%20Sets%20in%20DbCreator.md), 424-252) seed annotations, schemas, templates and configurations, but no key/value pairs. A seeded project has an empty values store, so the configurations that bind native pairs (`@db.host`, `@(db.password, secret)`, `@[https://@api.host/v1]`) stay unresolved after a seed, and a resolved read of a configuration that interpolates a missing pair fails with `422`. Demos, local development and the automation tests have to create the pairs by hand, through `sform story kv import` or the admin UI. The built-in set `exhibit` already ships a `key-values.json` that the seed ignores; its README tells to import it with `sform`.

Pairs were left out of 424-252 on purpose: DbCreator registers no key-encryption provider, so only `Public` pairs could be written, and secrets in a repository need their own rules.

A set should carry the pairs of its view in an optional `key-values.json`, in the format of the key/value export and import (`2s.key-values.v1`), and the seed should write them with the rules of the rest of a set: idempotent, `Skip` (an existing pair is never changed, a deleted pair stays deleted), and a dry run that writes nothing.

The open questions of the issue were answered, and this spec follows the answers:

1. `Standard` pairs are allowed in built-in sets and in sets loaded with `--path`.
2. No generator lines for pairs: a plain file is enough.
3. The showcase set gets `Public` and `Standard` pairs for `fabrikam/invoicing-suite` now, added to the admin UI's mock and written to `data-sets/showcase/key-values.json` by `showcase-set.test.ts`.

The issue has no comments (no CodeRabbit plan to weigh).

## Current State

- **The seed of a set.** `DataSetSeeder.SeedSetAsync` (`DbCreator/src/DataSets/DataSetSeeder.cs:363-457`): the hash and the record decide whether the set is read; `DataSetContent.Read` reads the documents and `annotations.jsonl`; the annotation files and the lines go in rounds through `IViewImporter` and `IAnnotationBulkWriter`; `CompleteRelationsAsync` completes the relation arrays; the schemas, templates and configurations go through `IViewImporter` with the same overlay (line 422); the record is written after a run without a `Failed` item (line 431). `Plan` (lines 62-101) checks every set's files from their sizes and counts the annotations of `annotations.jsonl` before anything is written; wrong input is a `DataSetException`, which `SeedCommand` maps to exit `2`.
- **The files of a set.** `DataSetFolder.GetEntries` (`DataSetFolder.cs:37-46`) hands every file but `set.json` and `annotations.jsonl` at the root to the import's reader, so a `key-values.json` counts as an ignored file today (`key-values` is no type code). `ComputeHash` covers every file that is not skipped, so a new or changed `key-values.json` changes the hash already.
- **Results and output.** `DataSetSeedResult` (documents per kind, `Lines`, `Items`, `RelationsCompleted`, `IsRecorded`), `DataSetSeedItem` (a listed outcome: `Path`, `Line`, a required `Kind` of `ViewDocumentKind`, the annotation's type and key, the outcome, code, message, warnings), `DataSetSeedRecord` (`Documents`, `Annotations`, and the outcomes of both), `DataSetOverview` for `sets`, and `Commands/DataSetOutput.cs` that prints them. `ExitCodes.Of` exits `4` for any `Failed` item.
- **The key/value import.** `IKeyValueService.ImportAsync(organization, project, view, KeyValueDocument, KeyValueImportOptions, KeyValueCaller)` (`Backend.Core/src/KeyValues/IKeyValueService.cs`), implemented by `CosmosKeyValueService.ImportAsync` (`Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs:456-557`):
  - at most `MaxImportItems` (1,000) items, and the format `2s.key-values.v1`, else `InvalidInputException` (`ImportInvalid`);
  - `ValidateImportItems` (lines 1243-1294): `KeyValueRules.EnsureKey`, `EnsureContentType`, the project policy's `AllowedProtections` (`ProtectionNotAllowed`), `EnsureValue` (24,576 bytes in UTF-8), the shape of a secret reference (`EnsureReference`: a reference is `Public`, `SecretReferences.Ensure`), a public item without a value, a key listed twice ignoring case;
  - `PlanImportItemAsync` (lines 1296-1331) plans against every stored pair, deleted ones included: a missing key is `Create`, an item without a value `Unchanged`, **a deleted key `Create`** (an import continues its history, so it restores it), a pair with the same level, content type and value `Unchanged` (a secret is decrypted for the comparison), otherwise `Update`;
  - any validation or plan error throws `KeyValueImportInvalidException` with the failing items, and nothing is written; `DryRun` returns the plan;
  - the writes go in transactional batches of 50 keys; a non-dry run writes one `Import` audit event, adds the writes to the usage meters, and the first `Standard` write records the project's Standard key in its policy (`RecordStandardKeyAsync`, through `IKeyValueMaintenanceService`).
  - `Merge` would therefore update a pair whose value differs and restore a deleted one: neither is what a seed may do.
- **The format.** `KeyValueDocument` / `KeyValueDocumentItem` (`Abstractions.Annotations/src/Model/KeyValues/KeyValueDocument.cs`): `Format`, `Items` with `Key`, `Protection`, an optional `Value` (omitted for a secret exported without values) and `ContentType`. `KeyValueProtection` is `Public`, `Standard`, `Hsm`, `Byok`, `Hyok`.
- **Protection.** `AddCosmosDbAnnotations` registers `EnvelopeValueProtector` (`Backend.CosmosDb/src/EntryPoint.cs:152`), which takes every `IKeyEncryptionProvider`; without one for a level, a write throws `ProtectionNotConfigured` (`EnvelopeValueProtector.cs:136-141`, `GetProvider`). `AddKeyVaultKeyProtection(configuration, environment)` (`KeyValues.Azure.KeyVault/src/KeyValueProtectionEntryPoint.cs:19`) registers the Standard provider when `AzureKeyVaults:{KeyValues:Protection:Standard:VaultName}` (`default`) is set, refuses `KeyValues:Protection:Emulator` in Production, and uses the managed identity in Production and `DefaultAzureCredential` elsewhere (`KeyVaultClients.CreateKeyClient`). The API calls it in `Api.Functions/src/Program.cs:119`; DbCreator references only `Backend.CosmosDb`. DbCreator's environment comes from the build (`#if DEBUG` in `Program.cs`): a Release build, and so the dotnet tool, is Production.
- **The caller.** `KeyValueCaller.System()`: the `System` actor, both secret rights, the `System` channel.
- **The local vault.** The Aspire host runs Lowkey Vault (`Aspire.Host/src/AppHost.cs:75-79`) and gives the API `AzureKeyVaults__default` and `KeyValues__Protection__Emulator=true`; the resource `db-seed` (`2sInitDb seed showcase`, lines 51-55) gets neither. `KeyValues.Azure.KeyVault/testint/LowkeyVaultFixture.cs` and `Backend.CosmosDb/test/KeyValueEndToEndTests.cs` start Lowkey Vault (`nagyesta/lowkey-vault:7.3.112`) with Testcontainers, each with a reuse label of its own, or use `LOWKEY_VAULT_URL`.
- **The showcase.** `ui.admin/src/app/mock/showcase/key-values.ts` seeds the values stores of five showcase scopes with `pair(key, level, versions, options)`; `fabrikam/invoicing-suite/default` has none. Several of its keys have hyphens (`tax.avalara.account-id`), which the platform's key rule refuses; the mock does not check them. `ui.admin/src/test/unit/mock/showcase-set.test.ts` writes the set `showcase` as file snapshots into `DbCreator/src/data-sets/showcase/` and fails on stale files.
- **The tests.** `DbCreator/test` (fakes of the importer, bulk writer and record store; `SeedFixture`; `BuiltInSetsTests` with `EverySetOfTheRepository_HasBindingsThatParse`) and `DbCreator/testint` (`DbCreatorFixture` over a Cosmos DB emulator, `SeedTestsBase`, `TestSetFolder`, `StoredItems`, `BuiltInSetsTests`), run in CI by `.github/workflows/Platform.Storyteller.CosmosDb.yml` on `ubuntu-latest` with Docker.
- **Docs.** `docs/Platform/Storyteller/data-sets.md` says "What a set cannot carry: key/value pairs …" (line 63) and does not list the set `exhibit`; `exhibit/README.md` and `exhibit/set.json` say the pairs come once 424-265 is done.

## Proposed Changes

Three phases, each builds and keeps the tests green: (A) the pairs in DbCreator with unit tests, (B) the vault, the integration tests and Aspire, (C) the showcase pairs, the built-in sets and the documentation.

### 1. `key-values.json` (DbCreator)

1. **`DataSets/DataSetKeyValues.cs`** (new): the pairs of a set.

   ```csharp
   public sealed record class DataSetKeyValues(IReadOnlyList<KeyValueDocumentItem> Items)
   {
       public const string FileName = "key-values.json";
       public const int MaxPairs = 10_000;
       public const long MaxFileBytes = 32L * 1024 * 1024;

       // the levels a set may hold
       public static readonly IReadOnlyList<KeyValueProtection> SeedableLevels = [KeyValueProtection.Public, KeyValueProtection.Standard];

       // the number of pairs per level in enum order, levels without pairs left out
       public IReadOnlyList<(KeyValueProtection Level, int Count)> Levels { get; }

       // null without the file; DataSetException when it is not valid
       public static DataSetKeyValues? Read(IFileSystem fileSystem, string folder);
   }
   ```

   - The file sits at the root of the set. One at a deeper path is an ignored file, as a nested `set.json` is.
   - **Reading.** `System.Text.Json` into `KeyValueDocument`: member names in any case, an unknown member refused (as in `set.json`), comments and trailing commas allowed, the levels as names in any case (`JsonStringEnumConverter` without integer values), the C# `required` members enforced. A file larger than `MaxFileBytes` is refused before it is read; a file that does not parse is refused with the reader's message (it names the path, line and position).
   - **The document.** `Format` is `2s.key-values.v1` when given (a missing one counts as it, as `sform story kv import` takes it); `Items` must be an array of objects, at most `MaxPairs` (the import of one call takes 1,000; the seed cuts them into chunks).
   - **Every item**, with the import's rules and messages where it has them, before anything is written:
     1. the key (`KeyValueRules.EnsureKey`: segments of letters, digits and `_`, at most 128 characters);
     2. the level `Public` or `Standard`; `Hsm`, `Byok` and `Hyok` are refused: they need per-organization keys, a customer's tenant or the project's recipients, which a seed cannot set up;
     3. a `Value`: an item without one is refused, because a seed creates the pair and has no existing secret to keep (an export without secret values cannot be seeded);
     4. the value size (`KeyValueRules.EnsureValue`: 24,576 bytes in UTF-8) and the content type (`EnsureContentType`: at most 255 characters);
     5. a secret reference (`KeyValueContentTypes.IsReference`) is `Public` and has the shape `SecretReferences.Ensure` checks;
     6. keys are unique ignoring case.
   - **The message** of `DataSetException` names the file and lists every problem, one per line, as `item 3 'smtp-host': …`, at most 20, then `… and N more`.
2. **`DataSetFolder.GetEntries`** leaves `key-values.json` at the root out of the reader's entries, like `set.json` and `annotations.jsonl`, so it is neither a document nor an ignored file. The hash is unchanged in its rules (it covers the file already).
3. **`DataSetContent`** gets `DataSetKeyValues? KeyValues` (read by `Read`), and **`DataSetOverview`** gets `KeyValues` too, for `sets`.

### 2. The levels before anything is written

1. **`DataSets/DataSetKeyValueSeeder.cs`** (new, registered by `AddDbCreator`) takes `IKeyValueService`, `IEnumerable<IKeyEncryptionProvider>`, `IOptions<KeyValueOptions>`, `ILogger`.
2. **`EnsureSeedable(DataSet set, DataSetKeyValues pairs)`**: a set with `Standard` pairs and no provider of the `Standard` level is a `DataSetException` whose message tells what to configure: `AzureKeyVaults__{VaultName}` (`default`) with the vault's URI, locally Lowkey Vault with `KeyValues__Protection__Emulator=true`, and the vault roles the API's identity has (Key Vault Crypto Officer and Key Vault Crypto Service Encryption User).
3. **`DataSetSeeder.Plan`** reads every set's `key-values.json` and calls `EnsureSeedable`, after the limits of its files and its lines. So an invalid file, a refused level, an item without a value and `Standard` pairs without a vault are wrong input (exit `2`) before the database is touched, as the issue asks.
4. The **project policy** of an existing project can allow fewer levels than the default (`Public`, `Standard`); only the database tells, so the import's own validation fails such a pair at seed time (`ProtectionNotAllowed`, exit `4`).

### 3. Seeding the pairs

`DataSetSeeder.SeedSetAsync` seeds the pairs after the annotations (the rounds, the relation completion) and before the schemas, templates and configurations, through `DataSetKeyValueSeeder.SeedAsync(DataSetTarget target, DataSetKeyValues pairs, bool dryRun, List<DataSetSeedItem> items, CancellationToken)`, with `KeyValueCaller.System()`:

1. **Chunks** of at most 1,000 items in file order, the most one import takes.
2. **The plan.** `ImportAsync` with `Mode = Merge` and `DryRun = true` validates the chunk with the policy of the project and plans every item against what is stored. A `KeyValueImportInvalidException` fails the items it lists (with their codes, for example `ProtectionNotAllowed`); the rest of the chunk is planned again without them, so one refused pair does not keep the others from being written.
3. **The decisions:**

   | Plan | Outcome | Code |
   | --- | --- | --- |
   | `Unchanged` | `Unchanged` | |
   | `Update` (another value, level or content type) | `Skipped`, nothing written | `KeyValueExists` |
   | `Create` of a key that is soft-deleted in the view | `Skipped`, nothing written | `KeyValueDeleted` |
   | `Create` of a missing key | `Created` (in a dry run: would be created) | |

   The deleted keys come from `ListAsync` with `State = Deleted` (all pages), read once per set and only when the plan has a `Create`: the plan alone cannot tell a deleted key from a missing one. The codes are DbCreator's (`DataSetKeyValueSeeder.KeyValueExists`, `KeyValueDeleted`), outcomes of a seed, not errors of the API.
4. **The write.** Unless it is a dry run, one more `ImportAsync` (`Merge`, not a dry run) of only the items decided `Created`; its results are the outcomes: `Create` is `Created`, an item with an error code `Failed`; `Update` and `Unchanged` are reported as they are (`Updated`, `Unchanged`): they happen only when another writer created the key between the plan and the write. No call when nothing is to be created, so a second run writes nothing, not even the `Import` audit event.
5. **Failures.** A `StorytellerException` (an invalid plan at write time, `ProtectionNotConfigured`, `KeyEncryptionUnavailable`), `KeyValueIntegrityException`, `KeyValueStorageException`, and the vault's `RequestFailedException` and `AuthenticationFailedException` fail the items of the call with the code (or the exception's type) and message; the seed goes on with the next chunk and the configurations, exits `4`, and writes no record, so a run again completes what is missing. Anything else ends the tool as a crash, as a failure of the bulk writer does.
6. **Cancellation** is checked between the calls (the import takes no token); a stop leaves no record, as for the other steps.
7. **The first `Standard` pair** of a project creates its key in the vault and records it in the project's policy (`RecordStandardKeyAsync`, inside the import), as an API write does.
8. **What is not done.** The usage recorder of DbCreator is never flushed, so the writes of a seed do not reach the usage meters (harmless); a dry run of several sets does not carry the pairs of an earlier set into a later one, so when two sets of one run hold the same key, the dry run reports the later `Created`, and the real run `Unchanged` or `Skipped` (as for configurations, never a failure); a pair purged, or past the deleted retention, is missing, and a later seed of a changed set creates it again.

### 4. Results, record and output

1. **`DataSets/DataSetKeyValuesResult.cs`** (new): `DataSetKeyValuesResult(int Pairs, IReadOnlyList<(KeyValueProtection Level, int Count)> Levels, ViewImportSummary Summary)`.
2. **`DataSetSeedResult.KeyValues`** (null without the file). The listed outcomes (all but `Created` and `Unchanged`) join `Items`, so `HasFailures`, `Failures` and `ExitCodes.Of` count a failed pair as any other failure (exit `4`).
3. **`DataSetSeedItem`**: `Kind` becomes optional (`ViewDocumentKind?`, null for a pair), and a pair's item has `Path = "key-values.json"`, `KeyValueKey` and `Protection` (new members).
4. **`DataSetSeedRecord.KeyValues`**: the pairs the seed read; `Created`, `Updated`, `Unchanged` and `Skipped` count the pairs too. Records written before stay readable (the member is 0).
5. **`DataSetOutput`**:
   - a seed prints `  key-values.json: 20 pairs (19 Public, 1 Standard): 19 created, 0 updated, 1 unchanged, 0 skipped, 0 failed` after the lines of `annotations.jsonl`; a failure prints as `key-values.json (smtp.host): ProtectionNotAllowed …`; the other outcomes join "Not created" by code (`1 Skipped KeyValueExists`);
   - the summary of the core seed counts the pairs;
   - `sets` prints `  key-values.json: 20 pairs: 19 Public, 1 Standard` (and that `Standard` pairs need a Standard vault to seed).

### 5. The vault in DbCreator

1. **`DbCreator.csproj`** references `KeyValues.Azure.KeyVault`.
2. **`Startup.ConfigureServices`** calls `services.AddKeyVaultKeyProtection(configuration, Environment)` after `AddCosmosDbAnnotations`. So `AzureKeyVaults__default` (and `KeyValues__Protection__*`) configure DbCreator as they configure the API. A Release build is Production: the emulator is refused at startup, and the vault is reached with the managed identity or the `AZURE_*` variables of `EnvironmentCredential`, as the API in Azure. `AddDbCreator` does not call it, so the Cosmos DB suites that call `AddDbCreator` keep their own registrations.

### 6. Aspire

`db-seed` gets `AzureKeyVaults__default` (the endpoint of the `keyvault` container) and `KeyValues__Protection__Emulator=true`, and waits for the vault, as `api-functions` does. The API and the seed share the vault, so the API decrypts what the seed wrote.

### 7. The showcase set

1. **`ui.admin/src/app/mock/showcase/key-values.ts`**: pairs for `fabrikam/invoicing-suite/default`: public settings of the invoicing (SMTP host and sender, the PDF renderer, a JSON of feature flags, the dunning limit), a secret reference, a deleted pair (which the set leaves out), and two `Standard` pairs with demo values. Every key follows the platform's key rule (no hyphens), because the set's pairs go through the import's validation.
2. **`ui.admin/src/test/unit/mock/showcase-set.test.ts`** writes `key-values.json` from them: the current version of every pair that is not deleted, `{ Key, Protection, Value, ContentType? }`, ordered by key, in a `2s.key-values.v1` document; it is a file snapshot like the other files, and the stale check covers it. The test also checks that every key matches the platform's key rule.
3. **The set's configurations** stay as the mock has them: binding them to the pairs would change the mock's showcase data and its snapshots beyond this issue. The resolved read of seeded pairs is checked by the integration tests (section 8) with the set `exhibit`, whose configurations bind its pairs, and with a test set.

### 8. Tests

1. **`DbCreator/test`** (no database):
   - `DataSetKeyValuesTests`: a valid file (members and levels in any case, comments, the counts per level), no file, a file too large, invalid JSON, an unknown member, another format, no items, too many items, and each refused item (key, level, no value, size, content type, reference, duplicate ignoring case), with the listing of problems;
   - `DataSetFolderTests`: `key-values.json` at the root is no entry, deeper it is ignored; it changes the hash;
   - `DataSetSeederTests` over a fake `IKeyValueService` (`FakeKeyValueService`: an in-memory store with the import's plan and deleted keys): the step order (after the annotations, before the configurations), the decisions of the table, only the created items written, nothing written in a dry run or on a second run, the chunks, the re-plan after refused items, a failure without a record, the record's counts, and `Plan` refusing `Standard` pairs without a provider before any write;
   - `SeedCommandTests` and `SetsCommandTests`: exit `2` for each refusal, before the database is touched; the listing of a set with pairs;
   - `BuiltInSetsTests`: the pairs of `showcase` (`Public` and `Standard`) and `exhibit` read without problems, and every built-in set reads its pairs.
2. **`DbCreator/testint`** (Cosmos DB emulator, and Lowkey Vault in Docker or `LOWKEY_VAULT_URL`): `DbCreatorFixture` starts the vault lazily, with its own reuse label `dbcreator-integrationtests`, and builds a second service provider with `AddKeyVaultKeyProtection` for it. `KeyValueSeedTests`:
   - public pairs without a vault, their values, the record, and a run with `--again` that changes no item;
   - `Standard` pairs refused up front without a vault (exit `2`, nothing written), and seeded with Lowkey Vault: the value reveals, the stored item holds no plaintext, the policy records the Standard key;
   - existing and deleted pairs skipped and never changed or restored, an unchanged pair, and a changed set that creates only the new pairs;
   - a dry run that writes nothing;
   - a refused level and an item without a value (exit `2`, nothing written);
   - a project policy without `Standard`: the `Standard` pair fails, the others are created, exit `4`, no record;
   - a resolved read of a configuration that binds the seeded pairs (`@key`, `@[…]`, `@secret(…)`).
   - `BuiltInSetsTests`: `showcase` seeds without failures with Lowkey Vault, and so does `exhibit`, whose resolved configurations then bind their pairs.

### 9. Documentation

- `docs/Platform/Storyteller/data-sets.md`: the file in "A set" (and no longer in "What a set cannot carry"), a section **key-values.json** (the format, the rules, the levels, the vault settings, the warning that sets hold demo and test values only, never real secrets, while a `kv://` reference holds none), the step in **Seeding a set** with the decision table, the record's member, the output, the exit codes, the built-in sets (`showcase` with pairs, `exhibit` added), Aspire, and the tests.
- `docs/Platform/Storyteller/key-values.md`: a line under Import and export that DbCreator seeds pairs.
- `docs/Platform/Aspire.Host/local-development.md`: `db-seed` gets the vault.
- `src/Platform/ui.admin/README.md` (the showcase set), `exhibit/README.md` and `exhibit/set.json` (the pairs are seeded now), and `CLAUDE.md` (the DbCreator line).
- `docs/github/workflows.md`: the Cosmos DB workflow's DbCreator integration tests also start Lowkey Vault.
