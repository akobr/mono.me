# Data Sets in DbCreator

Linear: [424-252](https://linear.app/424fun/issue/424-252/dbcreator-seed-named-data-sets-into-a-chosen-organization-and-project)

## Problem

DbCreator (`src/Platform/Storyteller/DbCreator`, the tool `2sInitDb`) builds the containers and seeds one fixed, hard-coded data set: about a dozen core annotations of `42dotnet` and the access of the base owner (plus the AuthKit staging accounts with `stagingSeed:enabled`). Nothing puts a realistic or a large amount of data into a database, which local development (Aspire), the UI and CLI automation tests, demos and performance checks all need. The alternatives are poor:

- `sform story import` needs a running API, a signed-in caller and an existing project. It runs as a queued job and writes one document at a time.
- Hand-written C# in `Backend.CosmosDb/src/Data.cs` upserts raw entities: no audit, no relation maintenance, and every run resets what it touches (relation arrays, the configuration of `cnt.42.system`, even a soft delete).

The tool should seed named **data sets**, kept in the repository as plain files in the format of the view export, into an organization and project that it creates when they are missing. A run must be idempotent, and large sets of simple annotations must be written in bulk.

The open questions of the issue were answered as follows, and this spec follows the answers:

1. A changed set is seeded again with `Skip`: what is missing is added, nothing is overwritten. No reset, no overwrite.
2. `annotations.jsonl` supports simple generators.
3. Sets define no users. A created organization and project get the default Owner only.
4. Sets live in the DbCreator project, and any folder can be loaded too (`--path`).
5. The admin UI's mock data and a set are kept in sync in a simple way.

## Current State

- **DbCreator.** `Program.cs` hosts one McMaster command, `Commands/InitDbCommand.cs` (`[Command("2sInitDb")]`, no options but `--version`), on the CLI Toolkit's generic host (`UseCommandLineApplication<InitDbCommand>`, `UseStartup<Startup>`, `ValidateOnBuild = false`). `Main` catches `UnrecognizedCommandParsingException` only; a `CommandParsingException` ends as a crash (exit 1). `Startup` reads `app.config.json` and environment variables, registers `IFileSystem`, `AddCosmosDbAnnotations(configuration, "cosmos")`, `CoreDbStructureBuilder` and `GetBaseAnnotationsReview`. Output goes through the static `Colorful.Console`.
- **`CoreDbStructureBuilder.BuildAsync()`** (`Logic/CoreDbStructureBuilder.cs`): the layout check (`IContainerFactory.FindLayoutProblemsAsync`, `DatabaseLayoutException`), the containers `core` and `organizations`, `Data.CreateCoreAnnotations` for `42dotnet` (project `main`, view `default`), then the access through `Data.SeedAccountAccessAsync`. Both Cosmos test fixtures (`Backend.CosmosDb/test/Startup.cs:110`, `Access.Certificates/testint/CosmosFixture.cs:69`) register `CoreDbStructureBuilder` by hand after `AddCosmosDbAnnotations` and call `BuildAsync()`. `Backend.CosmosDb/test/OrganizationsContainerStructureTests.cs` and `CosmosAnnotationAuditTests.cs` depend on the core seed.
- **`Data.CreateCoreAnnotations`** (`Backend.CosmosDb/src/Data.cs:15-53`) upserts raw entities built by `Create.cs` (Newtonsoft, no audit items, hard-coded relation arrays, configuration version 1 without history). Nothing else calls `Data` or `Create`.
- **The view import.** The passes live as private members of `ViewTransferService` (`Backend.Core/src/Transferring/ViewTransferService.cs:340-893`): `OrderByPass`, the pre-reads `ReadStoredAsync` / `ReadConfigurationKeysAsync`, `FillOverlay`, `Track`, `ImportFileOrConflictAsync`, `ImportFileAsync` and the tally `ImportProgress`. They are tied to the job only through the record's organization, project, view and id, and the progress saves. The per-document work is public: `IAnnotationService.ImportAnnotationAsync`, `IConfigurationSchemaService.ImportSchemaAsync`, `IConfigurationTemplateService.ImportTemplateAsync` and `IConfigurationService.ImportConfigurationAsync`. DbCreator resolves all four, but not `ViewTransferService`, which needs the job store, the archive storage and the queue. `ViewArchiveReader.Read` reads zip streams only.
- **Writes of annotations.** `CosmosAnnotationService.CreateCoreAsync` (`Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs:409-537`) writes, per new key, the annotation and its audit item in one transactional batch per depth wave (`BatchPlan`), and replaces the relation arrays of active parents outside the create in a final wave (`RelationsWave = 10`). Every create reads each key of the path; an import of one annotation costs about a dozen requests. `CreateAnnotationsAsync` and `CreateAnnotationsFromStringAsync` are sequential per key. `BatchPlan` (`Annotating/BatchPlan.cs`) has fixed limits (100 operations, 1.5 MB, 8 parallel batches) and no cancellation. `AllowBulkExecution` is not used anywhere.
- **Organizations, projects and views.** An organization is the access point `apt.{org}` and a project `apt.{org}.{project}` in `core`; the membership is mirrored in the account. `IAccessService.CreateAccessPointAsync` creates a missing organization with the project, refuses an organization the owner does not own (`AccessDeniedException`), refuses an existing project (`AccessPointExists`), and mirrors the access into the owner's account only when that account exists. Views other than `default` are registered by `IViewService.CreateViewAsync` (`ViewExists` when present). The data services check none of these.
- **Set-like data in the repository.** `Playground/data` is already in the export's folder format. The admin UI's `showcase` mock dataset is TypeScript (`ui.admin/src/app/mock/showcase/`), and the mock has a port of the archive names (`archive-names.ts`) and the annotation file writer (`annotationDocument` in `transfer-handlers.ts`).

## Proposed Changes

The work is delivered in phases, one commit series each; every phase builds and keeps the existing tests green.

### 1. A reusable view importer (Backend.Core)

1. **`IViewImporter` / `ViewImporter`** (`Backend.Core/src/Transferring/`, public):

   ```csharp
   public sealed record class ViewImportFileResult(ViewArchiveFile File, ViewImportResult Result);

   public interface IViewImporter
   {
       // The passes of an import over the files, in any input order: one pre-read per kind, the annotations (ancestors
       // first), the overlay, then schemas, templates and configurations. Yields each outcome after the overlay has
       // tracked it; stopping the enumeration stops the import. A file with an ErrorCode is Failed without a call; a
       // write conflict fails its file (Conflict); any other exception ends the enumeration. A given overlay is used and
       // filled (a caller can share one over sequential calls); without one, the call makes its own.
       IAsyncEnumerable<ViewImportFileResult> ImportAsync(
           string organization, string project, string view,
           IReadOnlyList<ViewArchiveFile> files, ViewImportOptions options,
           ViewImportOverlay? overlay = null, CancellationToken cancellationToken = default);
   }
   ```

   The private members listed above move to `ViewImporter` unchanged in behaviour (`ReadStoredAsync` takes organization, project and view instead of the record; the conflict log names the path instead of the job id). Constructor: `(IAnnotationService, IConfigurationSchemaService, IConfigurationTemplateService, IConfigurationService, ILogger<ViewImporter>)`, plus an internal one taking an `ILogger` for `ViewTransferService`.
2. **`ViewImportTally`** (public): `ImportProgress` without the job, with `Add(file, result)`, `Total`, `Processed`, `Summary`, `Summaries`, `Items`, `AreItemsTruncated`, and the same counting rules. The job keeps `Truncate` and its `Apply` to the record.
3. **`ViewTransferService`** keeps its 10-argument constructor and builds the importer from the same four services, so the three hand-made constructions in the tests stay as they are. The progress saves, the stop on lost ownership and the order of reads are unchanged (the enumeration is lazy, so the pre-reads still run after the first `ProcessedCount = 0` save).
4. **`AddViewImporter()`** (`ViewTransferEntryPoint.cs`): `TryAddSingleton<IViewImporter, ViewImporter>()`, called by `AddViewTransfers` and by DbCreator.
5. **Reading loose files.** `ViewArchiveEntry(string Path, long Length, Func<Stream> Open)` and `ViewArchiveReader.Read(IReadOnlyCollection<ViewArchiveEntry> entries, ViewTransferOptions options, IEnumerable<ViewArchiveContent>? include = null)`. The zip overloads map their entries to it, so naming, duplicates, limits and parsing stay one code path. Backend.Core keeps no file-system dependency; the caller opens the files.
6. **Tests**: `Backend.Core/test/Transferring/ViewImporterTests.cs` (any input order, failed files, one overlay shared over two calls, one pre-read per kind, stopping the enumeration, a conflict fails its file only) and reader tests of the entry overload. The existing job tests stay unchanged.

### 2. A bulk writer of annotations (Backend.Core + Backend.CosmosDb)

1. **`IAnnotationBulkWriter`** (`Backend.Core/src/Annotating/`, a separate interface so the fakes of `IAnnotationService` are untouched):

   ```csharp
   public interface IAnnotationBulkWriter
   {
       // Creates the annotations of one view that are not active, with their missing ancestors, in waves of
       // transactional batches (roots first), with the items, stamps, audit entries and relation arrays of a create.
       // Never changes an active annotation (Unchanged when equal, Skipped DocumentExists otherwise) and never re-creates
       // a deleted one (Skipped AnnotationDeleted, with what hangs on it). Re-runnable: a run after a failure completes it.
       Task<AnnotationBulkResult> CreateMissingAnnotationsAsync(
           string organization, string project, string view,
           IReadOnlyList<Annotation> annotations, AuditActor actor,
           AnnotationBulkOptions? options = null, CancellationToken cancellationToken = default);
   }

   public sealed record class AnnotationBulkOptions
   {
       public bool DryRun { get; init; }
       public int MaxParallelBatches { get; init; } = 4;
       public int MaxUnitsPerPlan { get; init; } = 2_000;   // the memory bound of one executed plan
       public IProgress<int>? Progress { get; init; }       // annotations committed so far
   }

   public sealed record class AnnotationBulkResult(
       IReadOnlyList<AnnotationBulkItem> Items,             // per input key, in input order
       IReadOnlyList<string> AncestorsCreated,
       int RelationUpdates);

   public sealed record class AnnotationBulkItem(AnnotationKey Key, ViewImportResult Result);
   ```

   The results reuse `ViewImportResult` and its codes (`Created` with the warning `AncestorCreated`, `Unchanged`, `Skipped` with `DocumentExists`, `AnnotationDeleted` or `DependencyFailed`, `Failed` with `InvalidAnnotation` or `DuplicateFile`), so a caller reports them like an import.
2. **`CosmosAnnotationService.Bulk.cs`** (a partial of `CosmosAnnotationService`, so the items are produced by the same `ToStoredEntity`, `Stamp`, serializer, `AnnotationAuditStore.Prepare` / `Write` as `CreateCoreAsync`). Registered as `IAnnotationBulkWriter` in `AddCosmosDbAnnotations`. The algorithm:
   1. Validate (`AnnotationRules`), normalize (`AnnotationFactory.Normalize`) and deduplicate the input; a duplicate key fails with `DuplicateFile`, an invalid annotation with `InvalidAnnotation` (and what hangs on it with `DependencyFailed`).
   2. Close the set over its ancestors and read every key of the closure in chunked `ReadMany` calls (annotation items with their ETags), and the audit items of the keys that will be created (a missing key may still have an audit, which continues as in `CreateCoreAsync`).
   3. Decide per input key in the order of the import (depth, type, key): active → `Unchanged` or `Skipped DocumentExists`; a deleted key or ancestor → `Skipped AnnotationDeleted`; otherwise the key and its non-active ancestors are created. An ancestor not in the input is created with the defaults of the first descendant that needs it (`AnnotationFactory.CreateDefault(..., copyFrom)`), with the audit cause `Ancestor`, as a sequential import does.
   4. Relation arrays are computed set-wise: a created parent gets the names of its created children; an active parent gets the missing names in one ETag-guarded replace in the relations wave (no audit entry, no `Modified` change, as in `AddRelationUpdates`). Active children of the closure that an active parent does not list are added too, which repairs a run whose relations wave failed.
   5. Unless it is a dry run, the units (annotation + audit, one partition) run in waves by depth and then the relations wave, each wave in plans of at most `MaxUnitsPerPlan` units with `MaxParallelBatches` parallel batches.
   6. A concurrency failure (409, 412) or a throttle left after the SDK's retries (429, honouring `RetryAfter`) runs the whole procedure again, up to 5 attempts. Keys committed by an earlier attempt are still reported `Created`.
3. **`BatchPlan`** gets `maxParallelBatches` (default 8) and a `CancellationToken` for `ExecuteAsync`; `AnnotationItemStore` gets a chunked read of nodes with or without audits. Nothing else changes in the existing paths.
4. **Tests** (`Backend.CosmosDb/test/CosmosAnnotationBulkWriterTests.cs`): the items equal those of an import of the same annotations into another view (ids, stamps, entry ids and system properties normalized away); existing keys are `Unchanged` / `Skipped`; arrays of active parents gain the new names; a deleted key or ancestor is skipped with its subtree; a dry run writes nothing; an orphan audit continues; a re-run after a failed relations wave repairs the arrays; a few thousand keys over many partitions.

### 3. Data sets in DbCreator

1. **A set** is a folder with a manifest `set.json`, documents in the export's folder format (`annotations/`, `configurations/`, `schemas/`, `templates/`; only the file names matter, version files and `export.json` are ignored, hidden paths skipped) and an optional `annotations.jsonl`. `set.json` (member names in any case, unknown members refused):

   ```json
   {
     "Name": "showcase",
     "Version": 1,
     "Description": "…",
     "Organization": "fabrikam",
     "Project": "invoicing-suite",
     "View": "default",
     "ProjectDescription": "…",
     "ViewDescription": "…",
     "DependsOn": [ "core" ]
   }
   ```

   `Name` defaults to the folder name; `Version` to 1; `Organization`, `Project` and `View` are the default target of the set (see 3.4).
2. **`annotations.jsonl`**: one JSON object per line; blank lines and lines starting with `//` are skipped. A line holds the members of an annotation file plus `AnnotationKey`, and is read with `ViewAnnotationDocument.Parse` / `TryRead`, so numbers keep their text and dates stay strings, exactly as a file. A **generator line** adds `$count` (and optionally `$start`, default 1): the line stands for `$count` annotations, with `{n}` replaced by the index and `{0…0}` by the index padded to that many digits, in the key and in every string of the line (`Title`, `Labels`, `Values`, …). At most 200 000 annotations per set.

   ```json
   {"AnnotationKey":"unt.payments.ledger","Title":"Ledger"}
   {"$count":500,"AnnotationKey":"sbt.customer-{000}","Title":"Customer {n}","Labels":["generated"]}
   {"$count":500,"AnnotationKey":"exe.customer-{000}.payments.prod"}
   ```

   A key in both `annotations.jsonl` and an annotation file fails the line with `DuplicateFile`.
3. **The catalog.** Built-in sets are in `DbCreator/src/data-sets/<name>/`, copied to the output (and the tool package) and found from `AppContext.BaseDirectory`. `--path <folder>` (repeatable) adds a folder: a folder with `set.json` is one set, otherwise each sub-folder with `set.json` is one. A name found twice is an error. `DependsOn` names sets of the catalog; dependencies are seeded first, each set once per run; a cycle or an unknown name is an error. Every file is read through the injected `IFileSystem`.
4. **The target.** One run has one target. `--organization`, `--project` and `--view` win; otherwise the named sets' manifests give them and must agree; the view defaults to `default`. Dependencies are seeded into the same target. Names are checked with `NameRules` before anything is written.
5. **The owner and the target's creation** (through `IAccessService` and `IViewService`, not `Data`):
   1. The owner is `baseAccount:id` (validated as today), or `--owner <id>`. Its account is created when missing (`CreateAccountAsync`; the base account with its user name and name, another id with the id as both).
   2. A missing organization and project are created with `CreateAccessPointAsync` (the owner as Owner, `ProjectDescription` from the first set). An existing organization that the owner does not own is refused (exit 2) instead of taken over. An existing project is used as it is: its access is not changed.
   3. A view other than `default` is registered when missing (`CreateViewAsync`, `ViewDescription`, the System actor).
6. **The seed record.** After a set is seeded without failures, the tool upserts a record in the `organizations` container, partition `{project}.meta`, id `seed.{view}.{set}`: `Organization`, `Set`, `View`, `Version`, `Hash` (SHA-256 of the set's files: relative paths and bytes in ordinal order), `Seeded` (an `AuditStamp` of the System actor) and the counts. It has no `ViewName`, `ProjectName` or `AnnotationType` member and its id doesn't start with `view.`, so view discovery and the queries of annotations never see it. A set whose record has the same version and hash is reported as already seeded and not read further, unless `--again`. Otherwise (no record, or a changed set) the set is seeded with `Skip`: what exists stays, what is missing is created. A run that stops halfway has no record, so the next run completes it.
7. **Seeding one set** (`DataSetSeeder`), with `ViewImportOptions { Actor = AuditActor.System, Existing = Skip, Force = --force, DryRun = --dry-run }` and one overlay for the set:
   1. the annotation files through `IViewImporter` (per-document import, ancestors first);
   2. the `annotations.jsonl` annotations through `IAnnotationBulkWriter`; lines below an annotation that failed in step 1 are `DependencyFailed`; in a dry run the keys it would create enter the overlay;
   3. the schemas, templates and configurations through `IViewImporter` with the same overlay;
   4. the record.

   The result per set is a `ViewImportTally` per kind plus the bulk counts; the command prints it.
8. **Commands.** The root keeps its behaviour and gets subcommands (`[Subcommand]` on `InitDbCommand`, no interactive prompt):
   - `2sInitDb` — as today: the layout check, the containers, the access of the base owner (and the staging accounts), then the built-in set `core` into `42dotnet` / `main` / `default` (3.9).
   - `2sInitDb seed [<set>…] [--path <folder>]… [--organization <org>] [--project <project>] [--view <view>] [--owner <id>] [--dry-run] [--force] [--again]` — checks the layout and ensures the containers first. Without set names, the sets of the `--path` folders are seeded.
   - `2sInitDb sets [--path <folder>]…` — lists the sets: name, version, description, default target, dependencies, documents per kind, `annotations.jsonl` lines and generated annotations, and where the set comes from.
   - Exit codes: `0` success (also "already seeded"), `-1` parsing (now also `CommandParsingException`), `2` wrong input (unknown set, invalid manifest, cycle, bad names, an organization the owner does not own), `3` database layout, new `4` (`ERROR_SEED_FAILED`) when a document failed. Warnings (`AncestorCreated`, …) never fail a run.
9. **The built-in sets.**
   - `core`: the annotations and the configuration of `Data.CreateCoreAnnotations`, as files. `CoreDbStructureBuilder.BuildAsync()` seeds it after the access, so its annotations now get audit items and real relation maintenance, and a second run no longer resets them. `Data.CreateCoreAnnotations` and `Create.cs` are removed.
   - `showcase`: the `fabrikam` / `invoicing-suite` / `default` scope of the admin UI's showcase, generated from the mock (3.11).
   - `large`: generator lines only, about 16 000 annotations in the shape of the mock's `large` dataset (6 responsibilities with units, 500 subjects with contexts, usages, executions and units of execution).
   - `Playground/data` gets a `set.json` (target `42dotnet` / `main` / `default`) and can be seeded with `--path`; `sform story import` counts the manifest as ignored.
10. **DI.** `AddDbCreator(this IServiceCollection, IConfiguration)` registers the options, `IFileSystem` (TryAdd), `AddViewImporter()`, the catalog, the seeder, the record store, `CoreDbStructureBuilder` and the review; `Startup` calls it after `AddCosmosDbAnnotations`. The two Cosmos fixtures call it instead of registering `CoreDbStructureBuilder` by hand.
11. **The admin UI's showcase.** A Vitest test (`ui.admin/src/test/unit/mock/showcase-set.test.ts`) writes the `fabrikam` / `invoicing-suite` / `default` scope as the set `data-sets/showcase` with `toMatchFileSnapshot` (frozen clock, `annotationDocument`, `getPath`), and fails when a file of the set is stale or missing, so `npm test` keeps the mock and the set together. `set.json` of the showcase says that the documents are generated. The `Platform.UI.Admin` workflow also runs on changes of that folder.
12. **Aspire.** An explicit-start resource `db-seed` runs `seed showcase` against the same Cosmos resource with the same `baseAccount__id`.

### 4. Tests and CI

1. `DbCreator/test` (xUnit v3, MTP, `MockFileSystem`): the manifest, the catalog (built-in and `--path` sets, dependency order, cycles, unknown and duplicate names), `annotations.jsonl` and generators, the folder entries and skipped paths, the hash, the target resolution.
2. `DbCreator/testint` (xUnit v3, MTP, the Cosmos emulator; `CosmosTestEmulator.cs` and `OrganizationQueryGuard.cs` linked from `Backend.CosmosDb/test`, its own reuse label): a first seed creates the organization, project, owner access and view and the documents; a second run reports "already seeded" and writes nothing; a partial seed is completed; a changed set adds only what is missing; a dry run writes nothing; an organization of another owner is refused; 10 000 generated annotations are written in bulk with correct relation arrays and audit entries; the `core`, `large` and `showcase` sets seed without failures.
3. Both projects are listed in `42.mono.slnx`. `Platform.Storyteller.CosmosDb.yml` runs `DbCreator/testint` in its vNext job (and in the classic job outside pull requests); the unit project runs with the solution.

### 5. Documentation

- New `docs/Platform/Storyteller/data-sets.md`: the tool, the set format, generators, the catalog, the target and owner, the record, the commands and exit codes, how to add a set, the built-in sets.
- `export-import.md` (the importer and the entry reader), `playground.md` (seeding with `--path`), `docs/Platform/Aspire.Host/local-development.md` (`db-seed`), `docs/Platform/development.md`, `annotation-lifecycle.md` (the core seed), `docs/github/workflows.md`, `CLAUDE.md` (DbCreator, the test projects, CI), the admin UI README (the showcase set).
- A review per phase in `docs/Platform/Storyteller/specs/reviews/`.

### 6. Out of scope

- Key/value pairs in sets: DbCreator registers no key-protection providers, so only `Public` values could be written; a later change can add `key-values.json`.
- Accounts and roles in sets (answer 3), resetting or overwriting a seeded set (answer 1), and the rework of `Data.SeedAccountAccessAsync` (424-161).
- `AllowBulkExecution`: transactional batches keep an annotation and its audit atomic and need no second client.
