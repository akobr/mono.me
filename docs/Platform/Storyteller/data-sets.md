# Data Sets of DbCreator

DbCreator (`src/Platform/Storyteller/DbCreator`, the tool `2sInitDb`) creates the Cosmos DB database of the platform and seeds **data sets** into it. A data set is a folder of documents in the format of a [view export](export-import.md), with a manifest `set.json`, an optional `annotations.jsonl` of simple or generated annotations, and an optional `key-values.json` of [key/value pairs](key-values.md). A seed writes a set into an organization, project and view, and creates them when they are missing.

A seed goes through the services of the API, as the `System` actor: the access model creates the organization and the project, the view registry registers the view, the [view importer](export-import.md#import-without-a-job) writes the documents, a bulk writer writes the lines of `annotations.jsonl` in transactional batches, and the [key/value import](key-values.md#import-and-export) writes the pairs. So seeded annotations have audit entries and relation arrays like any other, and a seed is idempotent: what exists stays, what is missing is created, and a set seeded already is not read again.

Use it for local development (the Aspire host seeds the set `showcase`), demos, the [exhibition](access.md#exhibition) of a deployment (the set `exhibit`), tests, and performance checks with many annotations.

The code is in:

- `src/Platform/Storyteller/DbCreator/src/`:
  - `Commands/`: the root command `InitDbCommand` and its subcommands `SeedCommand` and `SetsCommand`, and `DataSetOutput`, which prints the results;
  - `DataSets/`: the manifest (`DataSetManifest`), `annotations.jsonl` (`DataSetAnnotationLinesReader`), `key-values.json` (`DataSetKeyValues`, and `DataSetKeyValueSeeder`, which writes the pairs), the files and the hash (`DataSetFolder`), the catalog (`DataSetCatalog`), the target and the owner (`DataSetTarget`, `DataSetOwner`, `DataSetProvisioner`), the seed record (`DataSetSeedRecord`, `CosmosDataSetSeedRecordStore`) and the seeder (`DataSetSeeder`);
  - `Logic/CoreDbStructureBuilder.cs`: the layout check, the containers, the access of the base owner and the set `core`;
  - `DbCreatorEntryPoint.cs` (`AddDbCreator`), `Startup.cs` (the Standard vault, `AddKeyVaultKeyProtection`) and `ExitCodes.cs`;
  - `data-sets/`: the built-in sets;
- `Backend.Core/src/Transferring/`: the importer (`IViewImporter`, `ViewImporter`), `ViewImportTally` and the reader of loose files (`ViewArchiveEntry`), see [export-import.md](export-import.md#import-without-a-job);
- `Backend.Core/src/Annotating/`: `IAnnotationBulkWriter` with `AnnotationBulkOptions`, `AnnotationBulkResult` and `AnnotationBulkItem`;
- `Backend.CosmosDb/src/Annotating/CosmosAnnotationService.Bulk.cs`: the bulk writer;
- `Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs`: the key/value import, and `KeyValues.Azure.KeyVault`: the Standard vault;
- `DbCreator/test/` (unit tests, no database) and `DbCreator/testint/` (seeds against a Cosmos DB emulator, and Lowkey Vault for the `Standard` pairs);
- `src/Platform/ui.admin/src/test/unit/mock/showcase-set.test.ts`: writes the set `showcase` from the admin UI's mock.

The design and its decisions are in [the spec](specs/2026-11-12%20Data%20Sets%20in%20DbCreator.md) and its phase reviews; the key/value pairs in [their spec](specs/2026-11-15%20Key-Value%20Pairs%20in%20Data%20Sets%20of%20DbCreator.md) (424-265).

## What DbCreator does

| Command | Does |
| --- | --- |
| `2sInitDb` | The build of the database, as before: the layout check, the database and the containers `core` and `organizations` (with the settings of `organizations`), the access of the base owner to `42dotnet` and `42dotnet.main` (and the AuthKit staging accounts with `stagingSeed:enabled`), then the built-in set `core` into `42dotnet` / `main` / `default`, a line about the seed, and the review of the base annotations |
| `2sInitDb seed [<set>…]` | Seeds data sets into one target, created when missing (see [seed](#seed)) |
| `2sInitDb sets` | Lists the data sets with what they hold; reads the files only (see [sets](#sets)) |

The root command keeps what it did before. Only the core annotations changed: they are the set `core` now, seeded through the services. So they get audit entries by `System`, the creates maintain their relation arrays, and a second run finds the set seeded already and writes nothing. Before, every run upserted the items as raw entities and reset what it touched, the relation arrays, the configuration of `cnt.42.system` and even a soft delete. A soft-deleted core annotation now stays deleted.

`seed` does not seed the access of the base owner, the staging accounts or the set `core`, unless it is named.

## A set

A set is a folder:

```
invoicing/
  set.json                    the manifest
  annotations.jsonl           optional: simple and generated annotations
  key-values.json             optional: the key/value pairs of the view
  annotations/
    rst.payments.json
    unt.payments.ledger.json
  schemas/
    exe.schema.json
    rst.payments.usg.schema.json
  templates/
    exe.template.json
  configurations/
    rst.payments.conf.json
```

- **The documents** are named as in an export ([File names](export-import.md#file-names)). Only the file name tells the kind; the folders play no part. The reader is the import's (`ViewArchiveReader`), so a set reads as an archive does: annotation files keep the text of their numbers and their date strings, the first of two files of one document wins and the other fails with `DuplicateFile`, and a file that is not valid fails with its code.
- **Ignored files** are counted and never fail a seed: version files (`rst.payments.conf.3.json`), `export.json`, a `set.json` or `key-values.json` below the root, and any other name, such as a `README.md`.
- **Skipped paths** are neither read, nor counted, nor hashed: a path with a segment that starts with `.` (`.git`, `.vscode`, `.DS_Store`), and a first folder `__MACOSX`.
- **An unpacked export** of a view (`sform story export -x`, either format) is a set once it gets a `set.json`.
- **Limits.** A set holds at most 1,000,000 files and 2 GiB of document files, and a document at most 2 MB (2,097,152 bytes), one Cosmos DB item; a file that is not a document, such as a version file or a `README.md`, may be of any size. The upload limits of an import job do not apply. A seed checks them from the sizes of the files before it writes anything, and a set beyond them is wrong input (exit `2`), for `sets` too: a document over 2 MB refuses the whole set, with the file in the message, where an import job fails only that file.

What a set cannot carry: accounts and roles (a created organization and project get the owner only), the history of a document or a pair (only its current version is written), deleted annotations and pairs, pairs of the levels `Hsm`, `Byok` and `Hyok` (see [key-values.json](#key-valuesjson)), the project's key/value policy, and the stamps of other actors (everything is stamped by `System`).

### set.json

```json
{
  "Name": "invoicing",
  "Version": 1,
  "Description": "The invoicing of the documentation's example.",
  "Organization": "fabrikam",
  "Project": "invoicing",
  "View": "default",
  "ProjectDescription": "The B2B SaaS of the documentation.",
  "ViewDescription": "Production",
  "DependsOn": [ "core" ]
}
```

| Member | Default | Meaning |
| --- | --- | --- |
| `Name` | The name of the folder | The name of the set in the catalog, the argument of `seed`, and a segment of the id of its record: 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit |
| `Version` | `1` | 1 or more. A new version seeds the set again, even with the same files |
| `Description` | | Shown by `sets` |
| `Organization`, `Project`, `View` | none, none, `default` | The default target of the set (see [The target](#the-target)) |
| `ProjectDescription` | | The description of the project when the seed creates it |
| `ViewDescription` | | The description of the view when the seed registers it |
| `DependsOn` | none | Sets of the catalog that are seeded first, into the same target |

- Member names match in any case. An unknown member is refused, so a typo never passes as a default. Comments and trailing commas are allowed.
- Names are trimmed; a blank `Organization`, `Project` or `View` counts as none. `DependsOn` counts a name once and refuses an empty one.
- A missing or invalid `set.json` is wrong input (exit `2`), and the message names the file.

### annotations.jsonl

One JSON object per line: the members of an annotation file ([Annotations](export-import.md#annotations)) plus its `AnnotationKey`. Blank lines and lines that start with `//` are skipped.

```json
// the responsibility with its unit
{"AnnotationKey":"rst.payments","Title":"Payments","Labels":["billing"]}
{"AnnotationKey":"unt.payments.ledger","Title":"Ledger","UnitType":"file","UnitDefinition":"ledger.csv"}
// 500 subjects and an execution of each
{"$count":500,"AnnotationKey":"sbt.customer-{000}","Title":"Customer {n}","Values":{"customerNumber":"C-{00000}"}}
{"$count":500,"AnnotationKey":"exe.customer-{000}.payments.prod"}
```

- A line is read as an annotation file is (`ViewAnnotationDocument`): members in any case, numbers keep their text, and dates stay strings. The key's type code matches in any case. The project and the view come from the target.
- **A generator line** has `$count`, and optionally `$start` (default `1`). It stands for `$count` annotations, with the indexes `$start` to `$start + $count - 1`. In every string value of the line, at any depth, the key included:

  | Placeholder | Becomes | Index 7 |
  | --- | --- | --- |
  | `{n}` | The index | `7` |
  | `{0…0}` | The index padded with zeros to as many digits (wider when the index is) | `{000}` gives `007` |

  Member names are not changed. `$count` must be a whole number of 1 or more and `$start` one of 0 or more, as JSON numbers (`5`, not `5.0` or `"5"`). Both member names match in any case. The last index must not go beyond the largest whole number (9,223,372,036,854,775,807). With a `$count` of 2 or more, the key needs a placeholder, or every annotation of the line would get the same key. A line without `$count` is taken literally.
- **At most 200,000 annotations** per set, every line and every index counted (a line that cannot be read counts as one). The count runs before any line is expanded, and a set above it is wrong input (exit `2`), with the line where it goes over, before anything is written.
- **At most 2 MB per line**, the limit of a document file, counted in UTF-8 bytes of the line as written (a generator's annotations are a few bytes longer once their placeholders are replaced). A longer line makes the set wrong input (exit `2`), with its line number (`'annotations.jsonl' has a line of more than 2,097,152 bytes at line 7, the most a document holds.`), before anything is written.
- **Duplicates.** A key that an annotation file holds fails its line with `DuplicateFile` (`The file 'annotations/rst.payments.json' already holds the annotation rst.payments.`), and so does a key of an earlier line (`Line 3 of annotations.jsonl already holds the annotation …`).

A line that cannot be read fails with its line number:

| Code | When |
| --- | --- |
| `InvalidDocument` | The line is not a JSON object, has no `AnnotationKey`, has a `$count` or `$start` that is not a whole number in range, a generator key without a placeholder, or `$start` without `$count` |
| `InvalidKey` | `AnnotationKey` is not a string or not a key |
| `InvalidDocument`, `InvalidAnnotation` | The content breaks the rules of an annotation file; for a generator, per generated annotation |
| `DuplicateFile` | See above |

A failed line fails what hangs on it: the lines and the annotation files below its key are `Skipped` with `DependencyFailed`. A `DuplicateFile` fails nothing, as its first occurrence counts.

Lines and files can be mixed: lines for many simple annotations, which the bulk writer writes fast, and files for detailed ones below or above them (see [Seeding a set](#seeding-a-set)).

### key-values.json

The key/value pairs of the set's view, in the format of the key/value [export and import](key-values.md#import-and-export), `2s.key-values.v1`:

```json
{
  "Format": "2s.key-values.v1",
  "Items": [
    { "Key": "smtp.host", "Protection": "Public", "Value": "smtp.fabrikam.example" },
    { "Key": "features.flags", "Protection": "Public", "Value": "{\"ksef\":true}", "ContentType": "application/json" },
    { "Key": "peppol.apiKey", "Protection": "Public", "Value": "kv://fabrikam-prod/peppol-api-key", "ContentType": "application/vnd.2s.secret-reference" },
    { "Key": "smtp.password", "Protection": "Standard", "Value": "demo-only-value" }
  ]
}
```

- The file sits at the root of the set, next to `set.json`; it is neither a document nor an ignored file. One at a deeper path is an ignored file. An export of a values store with its secret values (`sform story kv export --include-secrets`, or the admin UI's "Key/value document (JSON)") is a good start.
- It is read as `set.json` is: member names and level names in any case, an unknown member refused, comments and trailing commas allowed. A missing `Format` counts as `2s.key-values.v1`, another is refused, and `Items` must be an array of pairs. At most 10,000 pairs and 32 MiB per file.
- **Every item** follows the rules of the import, and the refusals of a seed:
  1. the key: segments of letters, digits and `_` that start with a letter or `_`, joined by `.`, at most 128 characters (`smtp-host` is not a key);
  2. the level `Public` or `Standard` (see below);
  3. a `Value`: a seed creates the pair and has no existing secret to keep, so an item without one is refused (an export without secret values cannot be seeded);
  4. a value of at most 24,576 bytes in UTF-8, and a content type of at most 255 characters;
  5. a secret reference (the content type `application/vnd.2s.secret-reference`, or the Key Vault reference of App Configuration) is `Public` and has the shape of a reference (`kv://{vault}/{name}[/{version}]`, or the URI of a secret);
  6. keys are unique, ignoring case.
- A file that cannot be read or holds an item that breaks a rule makes the set wrong input (exit `2`), for `sets` too, before anything is written; the message lists every problem, at most 20:

  ```text
  ! The key/value pairs '…/key-values.json' cannot be seeded: 2 items are not valid; nothing was written.
    item 3 'smtp-host': The key 'smtp-host' is not valid: use segments of letters, digits and '_' that start with a letter or '_', joined by '.', at most 128 characters.
    item 5 'api.token': the level Hsm cannot be seeded: Hsm, Byok and Hyok need keys of the organization, a customer's tenant or the project's recipients, which a seed cannot set up; use Public or Standard
  ```

**The levels.**

| Level | In a set |
| --- | --- |
| `Public` | Always |
| `Standard` | Only when DbCreator has a Standard vault (below). Without one, a set with `Standard` pairs is wrong input (exit `2`) before anything is written, with a message that says what to configure; also when it is seeded already, because the plan decides from the files alone. Built-in sets and `--path` sets alike. With `--public-pairs-only` the set is not refused: its `Standard` pairs are `Skipped` with `PublicPairsOnly` (also with a vault) and the set is not recorded, so a later seed with a vault adds them |
| `Hsm`, `Byok`, `Hyok` | Refused (exit `2`): they need per-organization keys, a customer's tenant or the project's recipients, which a seed cannot set up |

The policy of an existing project decides too: a level it does not allow fails its pair when the set is seeded (`ProtectionNotAllowed`, exit `4`), and the other pairs are written. The default policy, which a project the seed creates gets, allows `Public` and `Standard`.

**The vault.** DbCreator registers the Key Vault providers as the API does (`AddKeyVaultKeyProtection` in `Startup`), so the API's settings configure it:

| Variable | Meaning |
| --- | --- |
| `AzureKeyVaults__default` | The URI of the Standard vault (the vault named by `KeyValues__Protection__Standard__VaultName`, `default`). The API and the seed must use the same vault, or the API cannot decrypt what the seed wrote |
| `KeyValues__Protection__Emulator` | `true` for Lowkey Vault: a dummy token and its self-signed certificate. Never in production |

The identity needs the roles the API's identity has on that vault: **Key Vault Crypto Officer** (it creates the project's key on the first `Standard` pair) and **Key Vault Crypto Service Encryption User** (wrap and unwrap). A build of DbCreator in Release, the dotnet tool among them, runs as Production: it refuses the emulator at startup, and reaches the vault with a managed identity or a service principal of `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` (`EnvironmentCredential`), as the API in Azure; a Debug build (`dotnet run`) uses `DefaultAzureCredential`, so an `az login` works. The Aspire resource `db-seed` gets the host's Lowkey Vault (see [Running it](#running-it)). The first `Standard` pair of a project creates its key in the vault and records it in the project's policy, as the first `Standard` write of the API does ([The Standard level and Key Vault](key-values.md#the-standard-level-and-key-vault)).

**Secrets in the repository.** A `Standard` value in a set is plain text in Git: in the repository, in every clone, and in the package of the tool. **A set holds demo and test values only, never real secrets.** For a real secret, use a reference: a `Public` pair whose value names a secret in the customer's own vault (`kv://{vault}/{name}` with the content type `application/vnd.2s.secret-reference`). It holds no secret, and the platform never resolves it.

## The catalog

A run can seed the sets of its catalog:

- **The built-in sets**: the folders with `set.json` in `data-sets/` next to the tool (`AppContext.BaseDirectory`), in ordinal order, hidden folders left out. The project copies `DbCreator/src/data-sets/**` to its output and into the tool package (`tools/net10.0/any/data-sets/`), and so to the output of the projects that reference DbCreator (the Cosmos DB suites and both DbCreator test projects).
- **The sets of `--path` folders**, in the given order (`--path` is repeatable; a relative path is relative to the current folder). A folder with `set.json` is one set; otherwise each of its sub-folders with `set.json` is one. A folder that does not exist or holds no set is wrong input.

A name found twice, in two folders or in a folder and among the built-in sets, is wrong input. `DependsOn` names sets of the catalog: a run seeds every set after its dependencies, each once. A cycle (`The sets depend on each other in a cycle: a -> b -> a.`) and an unknown name, which the message lists the catalog for, are wrong input. Every file is read through the tool's `IFileSystem`.

## The target

One run has one target: an organization, a project and a view.

1. `--organization`, `--project` and `--view` win.
2. Otherwise the manifests of the named sets give them, and they must agree. A set that names none does not disagree; the manifests of dependencies do not count.
3. The view is `default` when nothing names one.

A missing organization or project, and sets that disagree, are wrong input, and the message names the option to give. The dependencies go into the same target, whatever their own manifests say.

The names are checked before anything of the target is written, with the rules of the API (`NameRules`): the project always, the view unless it is `default`, and the organization when the seed creates it. The plan checks the project, the view and an organization with a dot (a name with a dot is never an organization, even when it is the key of an existing project, `42dotnet.main`) before the database is touched. Whether the organization is new only the database tells, so the name of an organization without a dot is checked when the seed reads its access point (step 1 below): after the database and its containers are ensured, and before the account, the organization or the project is written. An existing organization is used by its name, whatever the rules say today.

### The owner and the creation of the target

The **owner** is `--owner <account-id>`, or `baseAccount:id` without it (the environment variable `baseAccount__id`; DbCreator's legacy Entra ID `sub` when neither is set). It is the id the API keys the account by: under Entra ID the lower-case `{tid}:{oid}`, under AuthKit or Keycloak the `sub`, which has no `:`. An id with `:` that is not two lower-case GUIDs is wrong input. The base account gets the user name and name of the platform's owner; another id is its own user name and name until its person signs in.

The seed makes the target exist through `IAccessService` and `IViewService`, as the API does (`DataSetProvisioner`):

1. It reads the access points of the organization (`apt.{organization}`) and the project (`apt.{organization}.{project}`) in `core`.
2. A project that is missing in an organization that exists, and whose Owner is not the owner, is refused (exit `2`) before anything is written. A seed never takes over an organization. An existing project whose Administrator or Owner is not the owner is refused the same way, so a typo in the target never writes into a project of someone else.
3. The owner's account is created when it is missing, first, so that creating the project mirrors the membership into it. This also happens when the project exists.
4. A missing project is created with `CreateAccessPointAsync`, with the organization when that is missing too: the owner is their Owner, and the project gets the `ProjectDescription` of the first named set that has one, then of the dependencies. An existing project that the owner administers is used as it is: its access is not changed.
5. A view other than `default` is registered when it is missing (`CreateViewAsync`, with the `ViewDescription` and the `System` actor).

What another writer created in the meantime is used as it is. A dry run reads, reports what it would create, and writes nothing. Sets define no users: the owner is the only member a seed adds.

## The seed record

After a set is seeded without a `Failed` outcome, the seed upserts its record into the container `organizations`, next to the registered views:

| Member | Holds |
| --- | --- |
| `Organization`, `PartitionKey` | The organization and its partition `{project}.meta` |
| `id` | `seed.{view}.{set}` |
| `Set`, `View`, `Version` | The set, the view and the version of the set |
| `Hash` | The hash of the set's files |
| `Seeded` | An [audit stamp](audit-stamps.md) of the `System` actor |
| `Documents`, `Annotations`, `KeyValues` | The document files, the annotations of `annotations.jsonl` and the pairs of `key-values.json` the seed read (`KeyValues` is 0 in a record written before sets had pairs) |
| `Created`, `Updated`, `Unchanged`, `Skipped`, `Warnings` | The outcomes of all of them together |

The record has no `ViewName`, `ProjectName` or `AnnotationType`, and its id does not start with `view.`, so view discovery, the view registry and the queries of annotations never see it.

**The hash** is a SHA-256 over every file of the set that is not skipped (the manifest, `annotations.jsonl` and ignored files included), in the ordinal order of their paths: per file, its path, a NUL byte and the SHA-256 of its content. A text file counts with LF line ends, so a checkout with `core.autocrlf` hashes as one without. A file with a NUL byte among its first 8,000 bytes is binary (Git's test) and counts as it is.

| The target has | A seed |
| --- | --- |
| No record of the set | Seeds the set |
| A record with the same `Version` and `Hash` | Reports the set as seeded already and reads nothing more; exit `0`. `--again` seeds it anyway |
| A record of another version or hash: the set changed | Seeds the set: new documents and pairs are created, what exists stays. A changed document is `Skipped` with `DocumentExists`, a changed pair with `KeyValueExists`; neither is updated |
| No record after a run that stopped halfway: a crash, a stop, a failed document | Seeds the set: what the earlier run wrote is `Unchanged`, the rest is created, and the relation arrays the earlier run left incomplete are completed (see [Seeding a set](#seeding-a-set)) |

A seed never overwrites and never deletes. To change a seeded document, change it through the API, `sform` or the admin UI, or import it with `sform story import` (which overwrites by default). A deleted annotation is not re-created: it is `Skipped` with `AnnotationDeleted`, with what hangs on it. A deleted pair is not restored: it is `Skipped` with `KeyValueDeleted` (a pair purged, or past the deleted retention of 90 days, is gone, and a later seed of a changed set creates it again). To change a seeded pair, set it through the API, `sform story kv` or the admin UI, or import it with `sform story kv import`. The record is written only by a real run without a `Failed` outcome; warnings and skipped documents do not keep a set from being recorded.

## Seeding a set

`DataSetSeeder` seeds one set with the import options `Actor` = `System`, `Existing` = `Skip`, `Force` = `--force` and `DryRun` = `--dry-run`, and one [overlay](export-import.md#overlay) for the whole set, so each step sees what the steps before it made active or failed:

1. The hash and the record decide whether the set is seeded at all (see [The seed record](#the-seed-record)).
2. The documents and `annotations.jsonl` are read. A line that cannot be read is `Failed`, and its key joins the overlay's failed annotations before anything is written, so a file below it is `DependencyFailed` instead of creating the key with defaults.
3. **The annotations, in rounds.** A round imports its annotation files through `IViewImporter` (one document at a time, ancestors first), then sends its lines to `IAnnotationBulkWriter`. A set without a line above a file has one round: the files, then the lines. A file below a line comes in a later round than the line, and a line below a file comes after the file. Both the importer and the bulk writer create a missing ancestor with defaults, and `Skip` would then keep the defaults over the line or the file of that key.
4. A line on or below an annotation that failed, in a file or a line, is `Skipped` with `DependencyFailed` and is not sent.
5. **The relation arrays of the annotation files are completed** (`IAnnotationBulkWriter.CompleteRelationsAsync`, also in a dry run, which only counts). A create commits the key first and the arrays of its active parents in its last wave, and an import that finds the key active again reports it `Unchanged` without a look at the parents. So a run that stopped between the two, by a crash, a stop or a request that failed after the retries of the Cosmos DB client, would leave a parent without the name of its child, and the next run would record the set so. The keys of the files and their ancestors are read in reads of many items, and an active parent that misses an active child gets the name in one replace under its ETag, without an audit entry and without a change of `Modified`. On a first seed nothing is missing, and the step costs a read of many items per 1,000 keys. The seed prints the number of annotations it completed, when there are any. The lines need no such step: the bulk writer does the same for the keys of every call.
6. **The pairs of `key-values.json`** go through the key/value import (`IKeyValueService.ImportAsync`, from `DataSetKeyValueSeeder`) as the `System` caller (both secret rights, the `System` channel), before the configurations that bind them. The import's `Merge` would update a pair whose value differs and restore a deleted one, so the seed sends it neither. Per chunk of 1,000 pairs, the most one import takes:
   1. a dry run of the import plans the chunk: it validates the pairs with the project's policy and compares each with what the view holds, deleted pairs included (a `Standard` secret is decrypted for the comparison);
   2. the decisions:

      | The view holds | Outcome |
      | --- | --- |
      | No such key | `Created` |
      | The key with the same level, content type and value | `Unchanged` |
      | The key with another value, level or content type | `Skipped` with `KeyValueExists`; not changed |
      | The key soft-deleted | `Skipped` with `KeyValueDeleted`; not restored. The plan cannot tell a deleted key from a missing one, so the seed lists the deleted keys of the view, once per set and only when a key is to be created |

   3. a second import writes only the pairs decided `Created`, in transactional batches of 50. With nothing to create there is no call, so a run with `--again` writes no pair and no audit event.

   A pair the import refuses (`ProtectionNotAllowed` of the project's policy) is `Failed`, and the rest of its chunk is planned again without it. A failure of the vault (`KeyEncryptionUnavailable`, `ProtectionNotConfigured`, a `403` of the vault, a vault it cannot reach after the retries of the Azure SDK) fails the pairs of its call that are not stored: the import commits in batches of 50 and encrypts per batch, so the seed lists the active keys of the view after such a failure and reports the pairs of the earlier batches `Created` (that import writes no `Import` audit event); when the listing fails too, every pair of the call is `Failed` with the error of the write. A failed pair keeps the set from being recorded (exit `4`); the seed goes on with the configurations, and a run again writes the rest. A key that another writer creates between the plan and the write is updated by the write (`Updated`, listed). Each write is one `Import` event of the [audit trail](key-values.md#audit-trail), by `System`; the writes do not reach the [usage meters](key-values.md#usage-meters), because DbCreator never flushes its usage recorder.
7. The schemas, templates and configurations go through `IViewImporter`, with the same overlay.
8. The record is written (see above).

A run seeds its sets in the order of their dependencies. A set that depends on a set with failures is not seeded, and the run exits `4`; a set that does not depend on it is seeded. `seed` prints it under the heading of the set (`# b  version 1, built-in`) as `! Not seeded: the set 'a' it depends on failed.`

The outcomes, codes and warnings are those of an import ([Outcomes, codes and warnings](export-import.md#outcomes-codes-and-warnings)): `Created` (with the warning `AncestorCreated` when missing ancestors are created with defaults), `Unchanged`, `Skipped` (`DocumentExists`, `AnnotationDeleted`, `DependencyFailed`, `AnnotationNotFound` for a document whose annotation is missing), and `Failed` (`InvalidDocument`, `InvalidKey`, `InvalidAnnotation`, `DuplicateFile`, `SchemaViolation`, …). A pair is `Created`, `Unchanged`, `Skipped` (`KeyValueExists`, `KeyValueDeleted`) or `Failed` (`ProtectionNotAllowed`, `KeyEncryptionUnavailable`, …), and `Updated` only after a race (see step 6). Warnings never fail a seed. A configuration is validated against its schemas as in an import; `--force` stores schemas and configurations that break each other, with a warning.

**A dry run** decides every outcome and writes nothing. The overlay carries what the earlier steps of the set, and the sets before it in the run, would write, and the bulk writer is given those keys (`AnnotationBulkOptions.DryRunActiveKeys`), so a dry run reports the failures of the real run and exits as it would. The pairs are planned by the import's own dry run, which validates them with the project's policy as the real run does. The pairs an earlier set of the run would create are carried too, so a later set's pair of the same key is `Unchanged` or `Skipped` with `KeyValueExists`, as in the real run. One difference stays: when two sets of one run configure the same annotation differently, the dry run reports the later one `Created`, and the real run `Skipped` with `DocumentExists`. Neither is a failure.

### Bulk writing

`IAnnotationBulkWriter.CreateMissingAnnotationsAsync` creates the annotations of one view that are not active, with their missing ancestors. The Cosmos DB implementation is a part of `CosmosAnnotationService` (`CosmosAnnotationService.Bulk.cs`), so it writes the same items, stamps and audit entries as a create.

- **Decisions.** Every key is decided as a sequential import with `Skip` decides it, in the import's order (depth, type, key): an active key is `Unchanged` when it is equal and `Skipped` with `DocumentExists` otherwise; a deleted key, or one below a deleted ancestor, is `Skipped` with `AnnotationDeleted`, with what hangs on it; a missing key is `Created`. A missing ancestor that is not in the input is created with the defaults of the first key that needs it, with the audit cause `Ancestor` and the warning `AncestorCreated`. Invalid annotations fail with `InvalidAnnotation`, a key given twice with `DuplicateFile`. The project and view of the call win over those of the annotations.
- **Reads.** The keys and all their ancestors are read in reads of many items (at most 1,000 ids each). The audits are read only for the keys the writer creates, and not at all in a dry run.
- **Writes.** Each created key is one unit: its annotation item and its audit entry, in one transactional batch of its partition. The units run in waves by depth, roots first. Within a wave they are sorted by partition and cut into batches of at most 100 operations and about 1.5 MB, in plans of at most `MaxUnitsPerPlan` (2,000) units with `MaxParallelBatches` (4) batches at once. A last wave adds the new names to the relation arrays of active parents: one replace per parent, guarded by its ETag, without an audit entry and without a change of `Modified`, as a create does. It also adds active children that a parent does not list, which repairs a run whose last wave failed.
- **Retries.** A concurrent change (`409`, `412`) runs the whole procedure again. A throttle (`429`) that outlasts the retries of the Cosmos DB client waits its `RetryAfter` (without one, 1 s after the first attempt, 2 s after the second, and so on) and runs it again. After 5 attempts the call ends with a `Conflict` error or the `429`; the seed then stops, and a run again completes it. Keys an earlier attempt committed are still reported `Created`, also when an ancestor was deleted since (the key exists; what hangs on the deleted ancestor and is not written yet is `Skipped` with `AnnotationDeleted`).
- **Completion.** `CompleteRelationsAsync` is the last wave alone, for keys written before: it reads the keys and their ancestors and adds every active one to the arrays of its active parents that miss it, with the same replaces and retries. It creates nothing, and a missing or deleted key stays as it is. The seed calls it for the annotation files (see [Seeding a set](#seeding-a-set)).
- **Progress.** After each plan of a real run, the seed prints the number of annotations written so far, for example `… 2,506 annotations of annotations.jsonl written`. A plan holds at most 2,000 annotations of one depth, so the numbers follow the waves: for `large`, 506 (the depth 0: 6 responsibilities and 500 subjects), then 2,506, 4,506 and 4,517 (the 4,011 contexts, usages and units of depth 1), and so on to 16,017.

### Timings

Measured on the vNext emulator in a local Docker, from the phase reviews of the spec. They show the proportions, not the speed of Azure Cosmos DB:

| Run | Time |
| --- | --- |
| `2sInitDb` on a new database (`core`: 12 documents) | 1.0 s for the set, 4 s for the process |
| `2sInitDb` again (`core` seeded already) | 2.2 s for the process |
| `seed large` (16,017 annotations in bulk) on an empty database | 41 s |
| 10,000 generated annotations (`DbCreator/testint`) | 35 to 39 s |
| `seed large --dry-run` | 3.2 s |
| `seed large` again (seeded already, nothing read) | 0.6 s |
| `seed large --again` (16,017 unchanged) | 30 s |
| `seed showcase` (about 400 documents, one at a time) on an empty database | 24 to 25 s |
| The playground's folder (10 documents) | 0.6 to 4.4 s |

The vNext emulator slows down as the database fills: after 26,000 other annotations, `large` took 100 s and `showcase` 114 s.

## Commands

DbCreator reads `app.config.json` next to it, then the environment variables. The connection is `ConnectionStrings__cosmos` when set (the Aspire host sets it), otherwise `CosmosDb__Connection`; `app.config.json` points at the Windows emulator on `https://localhost:8081` and accepts its certificate (`CosmosDb__ShouldAcceptAnyCertificate`). The owner is `baseAccount__id`; `stagingSeed__enabled` is read by the root command only. The Standard vault of the pairs is `AzureKeyVaults__default`, with `KeyValues__Protection__Emulator=true` for Lowkey Vault (see [key-values.json](#key-valuesjson)). No command asks anything on the console.

### 2sInitDb

```
2sInitDb [-v|--version]
```

The build of the database (see [What DbCreator does](#what-dbcreator-does)). After the set `core` it prints one line, for example `The data set 'core' is seeded into '42dotnet/main/default' in 1.0 s: 12 created, 0 unchanged, 0 skipped, 0 failed.`, or that it is seeded already, with the failures. A failure of `core` exits `4` without the review.

### seed

```
2sInitDb seed [<set>…] [--path <folder>]… [--organization <organization>] [--project <project>] [--view <view>]
              [--owner <account-id>] [--dry-run] [--force] [--again] [--public-pairs-only]
```

| Argument or option | Default | Meaning |
| --- | --- | --- |
| `<set>…` | The sets of the `--path` folders | The sets to seed, built-in or of the `--path` folders; their dependencies are seeded first. Without names and without `--path`, nothing is seeded (exit `2`) |
| `--path <folder>` | | A folder with `set.json`, or a folder of such folders. Repeatable |
| `--organization` | The `Organization` of the named sets | The organization to seed into, created with the owner when missing |
| `--project` | The `Project` of the named sets | The project to seed into, created when missing; an existing one is used as it is when the owner is its Administrator or Owner |
| `--view` | The `View` of the named sets, otherwise `default` | The view to seed into, registered when missing |
| `--owner` | `baseAccount:id` | The account that owns a created organization and project |
| `--dry-run` | Off | Decide every outcome and write nothing of the target and the sets. The database and the containers are still ensured |
| `--force` | Off | Store schemas and configurations that break each other, with a warning |
| `--again` | Off | Seed a set that is seeded already in its version and with its files |
| `--public-pairs-only` | Off | Seed the `Public` pairs of `key-values.json` only: the `Standard` ones are `Skipped` with `PublicPairsOnly` and never sent, also with a vault, so a set with them seeds without a vault (instead of exit `2`). A set with a skipped pair is not recorded: a later seed with a vault adds its `Standard` pairs (and finds the rest `Unchanged`). The exit code stays `0` |

A seed:

1. **Plans** from the files and the options alone, so wrong input exits `2` before the database is touched: an unknown set, an invalid manifest, a cycle, a missing name of the target, an invalid project, view or organization with a dot, an invalid owner id, a missing `--path` folder, a set beyond the limits (a document or a line over 2 MB included), a `key-values.json` that cannot be seeded (an item that breaks a rule, a level other than `Public` or `Standard`, an item without a value), and `Standard` pairs without a Standard vault.
2. Prints the plan (`Seeding the set 'large' into 'acme/billing/default' for the owner … (#…).`), and in a dry run that nothing is written.
3. Checks the layout of the database and ensures the database and its containers, as the root command does (exit `3` for the earlier layout).
4. Makes the target exist and seeds the sets in order. What only the database tells is checked here, before anything of the target is written (exit `2`): the name of an organization the seed creates, and a missing project in an organization whose Owner is not the owner (see [The owner and the creation of the target](#the-owner-and-the-creation-of-the-target)).
5. Prints each set: its counts per kind, the line of `annotations.jsonl` with the ancestors the bulk writer created and the relation arrays it updated, the relation arrays it completed for the annotation files, the line of `key-values.json` (`key-values.json: 20 pairs (19 Public, 1 Standard): 20 created, 0 updated, 0 unchanged, 0 skipped, 0 failed`), the ignored files, the failures (at most 20, then `… and N more`; a pair as `key-values.json (smtp.host): ProtectionNotAllowed …`), the warnings by code, the other outcomes that are not `Created` or `Unchanged` by code, and the time. Then what the target got and the end:

```text
Seeding the set 'large' into 'acme/billing/default' for the owner Aleš Kobr (#user_01…).
Containers 'core' and 'organizations' are ready, the settings of 'organizations' are up to date.
  … 506 annotations of annotations.jsonl written
  … 2,506 annotations of annotations.jsonl written
  … 4,506 annotations of annotations.jsonl written
  … 4,517 annotations of annotations.jsonl written
  …
  … 16,017 annotations of annotations.jsonl written

# large  version 1, built-in
  annotations.jsonl: 49 lines (32 generators), 16,017 annotations: 16,017 created, 0 updated, 0 unchanged, 0 skipped, 0 failed, 0 with warnings
  Seeded and recorded in 41.3 s.

Created the organization 'acme' and the project 'acme.billing'.
Done in 43.2 s: 1 set seeded, 0 sets seeded already.
```

### sets

```
2sInitDb sets [--path <folder>]…
```

Lists the built-in sets and those of the `--path` folders: the name, the version and where it comes from (`built-in` or `--path`), the description, the default target (with a hint when the manifest names no organization or project), the dependencies (one that is not in the catalog is marked), the documents per kind with those that cannot be read and the ignored files, the lines, generators and annotations of `annotations.jsonl` with those that cannot be read, the pairs of `key-values.json` per level (`key-values.json: 20 pairs: 19 Public, 1 Standard (Standard pairs seed with a Standard vault only)`), and the folder. It reads the files only and never touches the database, so it lists `Standard` pairs without a vault. A set beyond the [limits](#a-set), or a `key-values.json` that a seed refuses, ends the listing as wrong input (exit `2`), as it ends a seed.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success, also for a set seeded already, and with warnings or skipped documents |
| `-1` (`255` on Linux and macOS) | The command line cannot be parsed: an unknown command, option or argument, a missing value, a single value given twice |
| `1` | The tool crashed, for example when the database cannot be reached or the bulk writer runs out of retries; the log file has the details |
| `2` | Wrong input: an unknown set, an invalid manifest, a set beyond the limits (its files, a document or a line of more than 2 MB, or more than 200,000 annotations), a `key-values.json` that cannot be seeded (it does not parse, more than 10,000 pairs, an item that breaks a rule of the import, a level `Hsm`, `Byok` or `Hyok`, an item without a value), `Standard` pairs without a Standard vault (unless `--public-pairs-only`), a cycle, a name found twice, a missing or invalid name of the target, a missing project in an organization whose Owner is not the owner, an existing project whose Administrator or Owner is not the owner, an invalid account id, a `--path` folder that does not exist or holds no set. For the root command, an invalid `baseAccount:id`. All before anything of the target is written; an invalid name of a new organization and the ownership of the organization and the project are known only after the database and its containers are ensured |
| `3` | The database has the containers of the earlier layout (one per organization); nothing was written |
| `4` | A document, an annotation or a pair of a set failed (also in a dry run that would fail), or a set was not seeded because a set it depends on failed |
| `40` | The seed was stopped (Ctrl+C, or the host shut down). The set that was running is not recorded, so a run again seeds what is missing |

## The built-in sets

| Set | Target | Content |
| --- | --- | --- |
| `core` | `42dotnet` / `main` / `default` | The platform's own annotations: 11 annotation files and the configuration of `cnt.42.system` |
| `large` | `acme` / `billing` / `default` | `annotations.jsonl` only: 49 lines, 32 of them generators, for 16,017 annotations |
| `showcase` | `fabrikam` / `invoicing-suite` / `default` | The scope of the admin UI's showcase mock: about 240 annotations, 150 configurations, two descendant-type schemas, a template, and 10 pairs (8 `Public`, 2 `Standard`) |
| `exhibit` | `fourth-wall` / `toons-and-tyrants` / `default` | The demo of Acme Corp and Globex: 30 annotations, 11 schemas, 3 templates gated per company with JSON-e, 30 configurations with every binding form, and 20 pairs (19 `Public`, 1 `Standard`); see its `README.md`. Also the content of a deployment's [exhibition](access.md#exhibition) |

The data of the [playground](playground.md) is a set too, outside the catalog: `src/Platform/Storyteller/Playground/data` with the `set.json` of the set `playground`.

### core

The subject `42` with its context `system`, the responsibilities `storyteller`, `supervisor` and `scheduler` with their usages and executions in `system`, and the configuration of `cnt.42.system` (`{ "IsSystem": true, "Number": 42, "Text": "system" }`). The annotation files are `{}`: they have no editable members. It is what DbCreator wrote by hand before (`Data.CreateCoreAnnotations`, now removed), without the hand-written relation arrays: the creates maintain them.

Every run of `2sInitDb` seeds it after the access, so the project `42dotnet.main` exists and is used as it is. The Cosmos DB suites build their database the same way (`AddDbCreator` and `CoreDbStructureBuilder.BuildAsync()`).

### large

The shape of the admin UI mock's large dataset (`npm run dev:mock -- --large`), in its scope `acme` / `billing` / `default`, so the admin UI against a seeded platform shows what the mock shows:

- 17 literal lines: the responsibilities `invoicing`, `payments`, `reporting`, `notifications`, `archiving` and `dunning`, with titles, descriptions and labels, and their 11 units with a `UnitType` and a `UnitDefinition`;
- 32 generator lines of 500: the subjects `customer-001` to `customer-500` (`Customer 1`, the label `generated`, `Values.customerNumber` `C-00001`), each with the contexts `prod` and `staging`, a usage of every responsibility, an execution per usage and context, and a unit of execution per unit in `prod`.

That is 6 + 11 + 500 × 32 = 16,017 annotations. Generators cannot draw, so the mock's random choices (other contexts, missing usages, labels and statuses) are left out. No relation array grows with the set: a subject lists 2 contexts and 6 responsibilities, and nothing lists the 500 subjects. Every ancestor is a line, so the bulk writer creates none on the way.

### showcase

The scope `fabrikam` / `invoicing-suite` / `default` of the admin UI's showcase mock (`npm run dev:mock`), the B2B SaaS of the documentation: the modules with their jobs, 22 customers with their environments, contracts, deployments and job runs, their configurations, a template and two descendant-type schemas. The Aspire resource `db-seed` seeds it.

**The documents are generated; do not edit them.** `src/Platform/ui.admin/src/test/unit/mock/showcase-set.test.ts` builds the scope under a frozen clock and writes it as a Git export of the view would hold it: an annotation file per active annotation, and the current version of every document. Each file is a file snapshot (`toMatchFileSnapshot`), so `npm test` fails when the mock and the set part, and a last test fails on the files the scope no longer gives. Change the mock instead, and regenerate the set:

```sh
cd src/Platform/ui.admin/src
npx vitest run test/unit/mock/showcase-set.test.ts -u
```

The path comes first, because `-u` takes an optional value in Vitest 4. Then delete the files the test reports as stale. `set.json` is the only file written by hand. The `Platform.UI.Admin` workflow also runs on changes of the set's folder, so a hand edit fails there. More in the [admin UI README](../../../src/Platform/ui.admin/README.md#mock-mode).

`key-values.json` is written by the same test, from the mock's values store of the scope (`app/mock/showcase/key-values.ts`): the current value of every pair that is not deleted, ordered by key, as the key/value export lists them. The test checks that every key follows the platform's key rule and that the levels are `Public` and `Standard`; the two `Standard` values are demo values. The modules bind them: `rst.invoicing` its mail (`@smtp.host`, `@smtp.sender`, `@(smtp.password, secret)`) and PDF renderer, `rst.payments` its Adyen account and HMAC key, and `rst.dunning` its reminders, so a resolved read of a seeded showcase shows the values. The bindings are top-level strings: in the mock's other views of the project, which have no pairs, they stay as stored.

What the mock has and the set does not: the stamps of its personas, the version histories, the deleted annotations and pairs, the project settings of the browser (type names, colors, time zone) and the members other than the owner. Its dates are absolute (from the frozen clock), so a scheduled annotation becomes active over time.

### exhibit

The demo of the platform ([its spec](specs/2026-11-13%20Demo%20Data%20Set%20of%20Acme%20Corp%20and%20Globex.md)): Fourth Wall Software and its customers Acme Corp and Globex in `fourth-wall` / `toons-and-tyrants` / `default`, written by hand. Its configurations bind its pairs in every form (`@key`, `@public(…)`, `@(…, public)`, `@secret(…)`, interpolations `@[…]`), so a resolved read answers `422` without them; its `key-values.json` holds 18 public pairs, a secret reference and one `Standard` pair with a demo value, so it seeds with a Standard vault only. `README.md` and `project-settings.json` (the type names for the admin UI, which neither a seed nor an import stores) are ignored files.

### exhibit

The demo of the platform: Fourth Wall Software delivers CRM, HR, CAD and Social solutions to its customers Acme Corp and Globex. The set holds 30 annotation files, 30 configurations that use every binding form, 11 schemas (type, annotation and descendant type) and 3 templates, gated per company with JSON-e. Its manifest targets `fourth-wall` / `toons-and-tyrants` / `default`. The model is in [the spec](specs/2026-11-13%20Demo%20Data%20Set%20of%20Acme%20Corp%20and%20Globex.md), and the commands are in the set's [README](../../../src/Platform/Storyteller/DbCreator/src/data-sets/exhibit/README.md).

Two files of the folder are not seeded. A seed counts them, and the `README.md`, as ignored files:

- `key-values.json`: 20 key/value pairs, 19 public ones and one `Standard` pair with a demo value. A set cannot carry key/value pairs until 424-265 is done, so import them with `sform story kv import`. Many configurations interpolate them, and a resolved read of such a configuration answers `422` without them.
- `project-settings.json`: the type names of the project (Company, Department, Solution, Application, AppUsage, Job, JobInstance) for the admin UI. Neither a seed nor an import stores them: until 424-266 is done, the admin UI keeps the settings of a project in the browser, where each user sets them.

**As the exhibition.** The set is also the content of a deployment's [exhibition](access.md#exhibition), the demo project that any user may join as a Reader. Seed it into the organization and the project that the `Exhibition` settings of the API name, with the account of `Exhibition:Owner` as the owner. In production the names are `exhibition` / `exhibition` ([decisions](../Deployment/decisions.md#the-exhibition)):

```sh
2sInitDb seed exhibit --organization exhibition --project exhibition --owner <account id>
```

- `--organization` and `--project` take the place of the names of the manifest. The view stays `default`, which is the view of the exhibition.
- `--owner` becomes the Owner of the organization and the project that the seed creates. The API offers the exhibition only while that account is the Owner of the project and no other account is its Administrator or Owner.
- Seed before the settings are set, then import the key/value pairs, and set the settings last: [Turning the exhibition on](../Deployment/runbook-azure-production.md#turning-the-exhibition-on) has the steps of production, and [The exhibition](../development.md#the-exhibition) those of a local API.
- Every visitor reads the versions of the documents with the name of whoever wrote them. The seed writes as `System`; a change by hand, the import of the key/value pairs included, carries the name of its author.
- A visitor sees the default type names, because `project-settings.json` reaches no browser but the one where somebody sets them.

### The playground

`2sInitDb seed --path src/Platform/Storyteller/Playground/data` seeds the set `playground` into `42dotnet` / `main` / `default`: the 5 annotation files and 5 configurations of the folder. The files name no responsibility, subject, context or execution, so the import creates those with defaults and the warning `AncestorCreated`. `sform story import` of the same folder counts `set.json` as an ignored file. See [playground.md](playground.md#prepare-the-data).

## Adding a set

A built-in set:

1. Create `src/Platform/Storyteller/DbCreator/src/data-sets/<name>/` with a `set.json`: the name, a version, a description, the default target and a `ProjectDescription`. The project file copies every file of `data-sets/` to the output and into the tool package; it needs no change.
2. Add the documents with the names of an export, or `annotations.jsonl`, and the pairs in `key-values.json`. An unpacked export of a view (`sform story export -x`) and an export of its values store with the secret values (`sform story kv export --include-secrets -o key-values.json`) are a good start; replace every real secret by a demo value or a reference (see [key-values.json](#key-valuesjson)).
3. Pick a target of its own: not `42dotnet`, which every run of `2sInitDb` seeds, and not the organizations of the automation tests (`ui-tests`, `cli-tests`).
4. Check it with `2sInitDb sets` and `2sInitDb seed <name> --dry-run`, which exits `4` when a document would fail.
5. The unit tests read every built-in set (`BuiltInSetsTests.EveryBuiltInSet_ReadsWithoutFailures`) and parse every binding of every set of the repository (`EverySetOfTheRepository_HasBindingsThatParse`) without a change. Add a test of its shape to `BuiltInSetsTests` of `DbCreator/test`, and a seed into an organization of its own to `BuiltInSetsTests` of `DbCreator/testint`.

A set elsewhere is any folder with a `set.json`, seeded with `--path`.

A change of any file changes the hash, so the next seed of a seeded target runs the set again with `Skip`: it adds what is new and keeps what is stored. A new `Version` does the same for a set whose files did not change.

## Running it

From the root of the repository, against the database of `app.config.json` (the Windows emulator) or of `ConnectionStrings__cosmos`:

```sh
dotnet run --project src/Platform/Storyteller/DbCreator/src                          # the database, the access and core
dotnet run --project src/Platform/Storyteller/DbCreator/src -- sets
dotnet run --project src/Platform/Storyteller/DbCreator/src -- seed large --dry-run
dotnet run --project src/Platform/Storyteller/DbCreator/src -- seed large
dotnet run --project src/Platform/Storyteller/DbCreator/src -- seed large --organization perf --project load-1
dotnet run --project src/Platform/Storyteller/DbCreator/src -- seed --path src/Platform/Storyteller/Playground/data
```

**Aspire.** The host of [local development](../Aspire.Host/local-development.md) has two resources of DbCreator, both started from the dashboard: `db-creator` runs `2sInitDb`, and `db-seed` runs `2sInitDb seed showcase` against the same emulator, with the API's Lowkey Vault (`AzureKeyVaults__default` and `KeyValues__Protection__Emulator=true`), so the `Standard` pairs of the set are encrypted with the key the API decrypts them with. The host passes its `BaseAccount:Id` user secret to both as `baseAccount__id`, so the same account owns `42dotnet` and the organization `fabrikam` the seed creates. Without it, when the host seeds the AuthKit staging accounts (the container emulator, or `StagingSeed:Enabled`), `db-seed` runs with `--owner user_01M41P6G2TTSXXMM88WNVMRQKG`, the default staging user whom `db-creator` makes a second owner of `42dotnet`, so the same local sign-in opens both; otherwise the owner is DbCreator's legacy Entra ID `sub`, which matches no account. The owner of `fabrikam` is fixed by the seed that creates it: a later seed, also with `--again`, uses the project as it is. To change it, reset the emulator's volume, or seed into another organization with `--organization`. `db-seed` ensures the containers itself and needs nothing of `core`; a second start reports the set as seeded already. To seed another set into the Aspire emulator, run DbCreator from the command line with the `ConnectionStrings__cosmos` that the dashboard shows in the environment of `db-creator`, and for `Standard` pairs the `AzureKeyVaults__default` and `KeyValues__Protection__Emulator` of `db-seed`:

```sh
ConnectionStrings__cosmos='<from the dashboard>' AzureKeyVaults__default=https://localhost:<port>/ KeyValues__Protection__Emulator=true \
  dotnet run --project src/Platform/Storyteller/DbCreator/src -- seed exhibit
```

Copy the vault's URI as the dashboard shows it, with `localhost`: Lowkey Vault puts the host of a request into the ids of its keys, a `Standard` value records the id of its key, and the API, which reaches the vault as `localhost`, refuses to unwrap with a key of another host (`The data key was wrapped by a key of vault '127.0.0.1', not of the configured vault.`). The same holds for any vault: the seed and the API need the same URI.

**The tool.** DbCreator packs as the dotnet tool `2sInitDb` (package `42.Platform.Storyteller.DbCreator`): `dotnet pack src/Platform/Storyteller/DbCreator/src` writes it to `.artifacts/`, the local feed of `nuget.config`. The package carries `app.config.json` and the built-in sets.

**Production.** The [deployment cookbook](../Deployment/cookbook-azure-production.md#162-the-database-dbcreator-once-per-schema-change) runs the root command once per schema change, which seeds `core`. The set `exhibit` is seeded there once, as the [exhibition](access.md#exhibition) of the deployment ([runbook](../Deployment/runbook-azure-production.md#turning-the-exhibition-on)). The other sets are for development, demos and tests.

## Tests

| Project | What it checks |
| --- | --- |
| `DbCreator/test` (`42.Platform.Storyteller.DbCreator.UnitTests`) | The manifest, `annotations.jsonl` and its generators, the files, the hash, the catalog, the target, the owner, the provisioning, `key-values.json` and its refusals, the seeder over fakes of the importer, the bulk writer, the key/value service and the record store (the step of the pairs: its place, decisions, chunks, re-plans and failures, and the refusal of `Standard` pairs without a vault), the command line and the commands, and every set of the repository (their shape, their pairs, that they read without failures, and that their bindings parse). No database |
| `DbCreator/testint` (`42.Platform.Storyteller.DbCreator.IntegrationTests`) | Seeds against a Cosmos DB emulator through the tool's own registrations: a first seed with the target, the owner's access, the view and the documents; a second run that writes nothing; a partly seeded target and a run that stopped halfway (also between the creates of files and their relation arrays), completed without overwriting; a changed set; a dry run; the refusal of another owner's organization; 10,000 generated annotations in bulk with their relation arrays and audit entries; the pairs (`KeyValueSeedTests`): `Public` pairs without a vault and `Standard` pairs with Lowkey Vault (refused up front without it), existing and deleted pairs skipped, a changed set, a dry run, the refused levels, a project policy without `Standard`, and a resolved read that binds seeded pairs; and the sets `core`, `large`, `showcase` (with the vault), `exhibit` (with the vault, and resolved reads of its bindings) and the playground |
| `Backend.CosmosDb/test` | The bulk writer (`CosmosAnnotationBulkWriterTests`: equal to an import of the same annotations, retries, throttles, cancellation, thousands of keys, the completion of relation arrays after an interrupted import), and `core` through `OrganizationsContainerStructureTests` |
| `Backend.Core/test` | The importer (`ViewImporterTests`), the tally and the reader of loose files |

```sh
dotnet test --project src/Platform/Storyteller/DbCreator/test
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/DbCreator/testint
```

`DbCreator/testint` deletes the database `42.Platform.2S` of the emulator it uses: the Windows emulator on `https://localhost:8081` unless `STORYTELLER_TESTS_USE_CONTAINER=true` starts a container (vNext by default, its own reuse label `dbcreator-integrationtests`). Its tests of `Standard` pairs (`KeyValueSeedTests`, and `showcase` and `exhibit` in `BuiltInSetsTests`) start Lowkey Vault in Docker on first use, with the emulator of either kind (its own reuse label `dbcreator-integrationtests`), or use a running instance named by `LOWKEY_VAULT_URL`; so the suite needs Docker in any case. CI runs both projects in the vNext job of `Platform.Storyteller.CosmosDb.yml` ([workflows](../../github/workflows.md)), and the integration tests also in its classic job outside pull requests.
