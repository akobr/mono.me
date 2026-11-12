# Phase 3 of Data Sets in DbCreator

## Overview

Phase 3 of the spec [Data Sets in DbCreator](../2026-11-12%20Data%20Sets%20in%20DbCreator.md) (424-252) covers the library part of section 3 in DbCreator (`src/Platform/Storyteller/DbCreator/src/DataSets/`): the set manifest, `annotations.jsonl` with generator lines, the folder reader and the content hash, the catalog of built-in and `--path` sets, the target and owner, the provisioning of the target through `IAccessService` and `IViewService`, the seed record, the seeder `DataSetSeeder` on the importer of phase 1 and the bulk writer of phase 2, and `AddDbCreator`. The core data moved too (3.9, set `core`), because both Cosmos fixtures depend on `CoreDbStructureBuilder.BuildAsync()`. The unit tests of section 4.1 are the new project `DbCreator/test`. The commands (3.8), the sets `large` and `showcase`, the Playground manifest, the admin UI's showcase (3.11), Aspire (3.12), `DbCreator/testint` and the documentation (section 5) are later steps. It is the commits `feat(platform): seed data sets with DbCreator (424-252)`, `refactor(platform): seed the core annotations as the built-in set core (424-252)` and `test(platform): cover the data sets of DbCreator (424-252)`.

## What Was Done

### 3.1 The set and its manifest

- `DataSetManifest.cs`: the record of `set.json` (`Name`, `Version`, `Description`, `Organization`, `Project`, `View`, `ProjectDescription`, `ViewDescription`, `DependsOn`) and `Read(IFileSystem, folder)`, with System.Text.Json: member names in any case, `JsonUnmappedMemberHandling.Disallow`, comments and trailing commas allowed. `Name` defaults to the folder name, `Version` to 1; the target names are trimmed (blank is none), `DependsOn` trimmed and distinct.
- **Additions:** the name must pass `NameRules.IsValidName` (2 to 63 lower-case letters, digits or hyphens), because it is a segment of the record's id and an argument of the command; `Version` must be 1 or more; an empty name in `DependsOn` is refused. Every problem, a missing `set.json` included, is a `DataSetException` with the path (`DataSetException.cs`: the wrong input of a seed, exit 2 in the command).
- `DataSet.cs` (`Manifest`, full `Folder`, `Source`) and `DataSetSource.cs` (`BuiltIn`, `Path`).

### 3.2 `annotations.jsonl` and generators

- `DataSetAnnotationLinesReader.cs`: `Read(IFileSystem, folder, target)` (null without the file; `File.OpenText`, so a BOM is fine) and `Read(TextReader, target, source)`. Blank lines and lines starting with `//` (after trimming) are skipped. Each line goes through `ViewAnnotationDocument.Parse` and the annotation through `ViewAnnotationDocument.TryRead` with the target's `FullKey`, so numbers keep their text and dates stay strings, and the `AnnotationKey` member stays in the document, which `TryRead` checks against the key as in a file.
- A line with `$count` (and `$start`, default 1; both matched in any case and removed) is a generator: per index, `Generate(document, index)` replaces `{n}` with the index and `{0…0}` with the index padded to that width (wider when the index is) in every string value at any depth (`Title`, `Labels`, `Values`, the key). Member names are not substituted. A line without `$count` is literal.
- The results are `DataSetAnnotationLine` (`Line`, `Key`, `Annotation` or `ErrorCode` and `Message`) in `DataSetAnnotationLines` (`Annotations`, `Lines`, `GeneratorLines`). Codes: `InvalidDocument` for a line that is not a JSON object, has no `AnnotationKey`, or a `$count` that is not a whole number of 1 or more (`$start`: 0 or more, `$start` without `$count`); `InvalidKey` for a key that does not parse (the type code in any case is lowered as in a file name; empty segments are refused); otherwise the codes of `TryRead` (`InvalidDocument`, `InvalidAnnotation`), per generated annotation.
- **Decision:** the limit of 200 000 annotations per set is checked over all lines before any line is expanded (a failed line counts as one), so an oversized generator costs nothing; beyond it `DataSetException` names the file and the line.
- `DataSetContent.cs`: `Read(IFileSystem, folder, target)` gives the documents and the lines, and `WithoutDuplicates` fails with `DuplicateFile` a line whose key an annotation file holds (any annotation file, a failed one too: `The file '…' already holds the annotation …`) or an earlier line holds (`Line N of annotations.jsonl already holds the annotation …`), as the reader does with a second file of one key.

### 3.3 The folder reader, the hash and the catalog

- `DataSetFolder.cs`:
  - `GetFiles(IFileSystem, folder)`: every file below the folder as `DataSetFile` (path with `/`, full path), in ordinal order; a path that `ViewArchiveNames.IsSkippedPath` skips is not entered.
  - `GetEntries`: the files as `ViewArchiveEntry` (length from `FileInfo`, `Open` through the file system), without `set.json` and `annotations.jsonl` at the root, so they are not counted as ignored (a nested `set.json` is, like any other non-document).
  - `ReadDocuments`: `ViewArchiveReader.Read(entries, ReaderOptions)`, with `ReaderOptions` sized for sets: `MaxArchiveEntries` 1 000 000, `MaxExtractedBytes` 2 GiB, `MaxDocumentBytes` the 2 MB default.
  - `ComputeHash`: SHA-256 (lower-case hex) over, per file in path order, the UTF-8 path, a NUL byte and the SHA-256 of the content. **Decision:** the content goes in as its own hash rather than its bytes, so no path and content can run into each other. Text files count with CRLF made LF (`NormalizeLineEnds`); a file is binary, and stays as it is, with a NUL byte among its first 8 000 bytes, Git's test, so a checkout with `core.autocrlf` and one without hash alike. Every file that is not skipped counts: the manifest, `annotations.jsonl`, and ignored files such as a README.
- `DataSetCatalog.cs` (singleton): `DataSetCatalog(IFileSystem, string? baseDirectory = null)`, the built-in folder `data-sets` below `baseDirectory ?? AppContext.BaseDirectory` (the container uses the default; tests give their own). `GetSets(paths)`: the built-in sets (sub-folders with `set.json`, hidden ones left out, in ordinal order; none when the folder is missing), then per `--path` folder in the given order (resolved with `Path.GetFullPath`): the folder itself when it has `set.json`, otherwise each sub-folder with one. A missing folder, a folder without any set and a name found twice are `DataSetException`s. `Resolve(sets, names)` (static): a depth-first walk over `DependsOn`, every set after its dependencies and once, the names in their order; a cycle names its path (`a -> b -> a`), an unknown name or dependency lists the catalog.
- `DbCreator.csproj`: `<None Update="data-sets\**\*" CopyToOutputDirectory="PreserveNewest" />`. The sets reach the output, the tool package (`tools/net10.0/any/data-sets/…`, checked with `dotnet pack`) and the output of the projects that reference DbCreator (both Cosmos suites and `DbCreator/test`).

### 3.4 The target

- `DataSetTarget.cs` (`Organization`, `Project`, `View`, `IsDefaultView`, `ProjectKey`): `Resolve(sets, organization, project, view)` takes the given names first (blank is none), otherwise the values of the named sets' manifests, which must agree (a set without one does not disagree; dependencies do not count); the view defaults to `default`. A missing organization or project, or manifests that disagree, are `DataSetException`s that name the option to give.
- `EnsureNames(isNewOrganization)`: `NameRules.EnsureOrganizationName` only for a new organization, `EnsureProjectName` always, `EnsureViewName` unless the view is `default`; an `InvalidInputException` becomes a `DataSetException`. The provisioning calls it after reading the access points and before any write.

### 3.5 The owner and the target's creation

- `DataSetOwner.cs`: `Resolve(ownerId, BaseAccountOptions)`: the given id, or `baseAccount:id` (the legacy default without one); an id with `:` must be `UserIdentity.IsEntraAccountId`, as `HasValidAccountId` checks the base account. The base account gets `Constants.BASE_ACCOUNT_USER_NAME` and `BASE_ACCOUNT_NAME`, another id is its own user name and name.
- `DataSetProvisioner.cs` (a class of its own, so the seeder stays about the sets) and `DataSetProvisioning.cs` (`IsAccountCreated`, `IsOrganizationCreated`, `IsProjectCreated`, `IsViewRegistered`; in a dry run: would be). `ProvisionAsync(target, owner, projectDescription, viewDescription, dryRun)`:
  1. reads `apt.{org}` and `apt.{org}.{project}`, then `EnsureNames`;
  2. **refinement of the spec:** a missing project in an existing organization whose Owner is not the owner is refused with a `DataSetException` before anything is written (no takeover); an existing project is used as it is, its access unchanged, whoever owns it;
  3. creates the owner's account when missing (`CreateAccountAsync`, `AccountExists` caught), first, so the project's membership is mirrored into it;
  4. creates the organization and the project when the project is missing (`CreateAccessPointAsync` with the owner and the description; `AccessPointExists` caught, an `AccessDeniedException` of a race becomes a `DataSetException`);
  5. registers a view other than `default` when `GetViewsAsync(…, discover: false)` does not list it as registered (`CreateViewAsync` with the description and `AuditActor.System`, `ViewExists` caught). Reading first lets a dry run tell.
- **Decision:** the account is ensured even when the project exists. The descriptions come from the first named set that has one, then from the dependencies: a dependency such as `core` is not what the project is about.

### 3.6 The seed record

- `DataSetSeedRecord.cs`: the stored item itself: `Organization`, `PartitionKey` (`ViewEntity.GetPartitionKey(project)`, `{project}.meta`), `id` `seed.{view}.{set}`, `Set`, `View`, `Version`, `Hash`, `Seeded` (an `AuditStamp` of `AuditActor.System`) and the counts `Documents`, `Annotations`, `Created`, `Updated`, `Unchanged`, `Skipped`, `Warnings` (files and lines together). No `ViewName`, `ProjectName` or `AnnotationType`.
- `IDataSetSeedRecordStore.cs` and `CosmosDataSetSeedRecordStore.cs`: a point read (`ReadItemAsync`, 404 is null) and an upsert in the organization's partition, both with the client's serializer.
- What else reads `{project}.meta` (grep of `.meta`, `GetPartitionKey(project)` and the project-wide queries): the view registry reads it with `STARTSWITH(c.id, 'view.')`; view discovery needs `ViewName`; the project-wide queries of annotations need `ProjectName`, `ViewName` or `AnnotationType`; the key/value queries need `.kv.` in the partition key. None sees the record. **Note:** the configuration listings of a view query by id prefix (`{view}.cnf.`, `{view}.cfv.`) over the project's partitions, so a record of a view named `cnf` or `cfv` would match the listing of a view named `seed` (`seed.cnf.{set}`). It takes both view names in one project; the id is the spec's, so it stays.

### 3.7 Seeding one set

- `DataSetSeeder.cs` (`IFileSystem`, `DataSetCatalog`, `DataSetProvisioner`, `IViewImporter`, `IAnnotationBulkWriter`, `IDataSetSeedRecordStore`, `IOptions<BaseAccountOptions>`, `TimeProvider`, `ILogger<DataSetSeeder>`; no console output):
  - `SeedAsync(DataSetSeedRequest)`: the catalog with the request's `Paths`; the named sets, or without names the sets of the folders (none at all is a `DataSetException`); `Resolve`; the target from the named sets; the owner; `ProvisionAsync`; then every set in dependency order. **Addition:** a set that depends on a set with failures is not seeded (`FailedDependency`), since its annotations would hang on what is missing. Returns `DataSetRunResult` (`Target`, `Owner`, `Provisioning`, `Sets`, `IsDryRun`, `HasFailures`). The containers must exist; the command calls `EnsureContainersAsync` first.
  - `SeedSetAsync(set, target, DataSetSeedOptions)`: computes the hash and reads the record; same version and hash without `Again` is `IsAlreadySeeded` and nothing else is read. Otherwise `DataSetContent.Read`, `ViewImportOptions { Actor = AuditActor.System, Existing = Skip, Force, DryRun }` and one `ViewImportOverlay`:
    1. the annotation files through `IViewImporter`;
    2. `annotations.jsonl` (`WriteLinesAsync`): a line that could not be read is `Failed` (its key joins `overlay.FailedAnnotations`, unless it is a `DuplicateFile`); in depth order, a line whose key or an ancestor is in `FailedAnnotations` is `Skipped DependencyFailed` with the importer's words (`The ancestor(s) … failed in this import.`) and joins it, without being sent; the rest go in line order to `CreateMissingAnnotationsAsync` (`AnnotationBulkOptions { DryRun, Progress = AnnotationProgress }`); a `Failed` or `DependencyFailed` result joins `FailedAnnotations`; in a dry run the `Created` keys and `AncestorsCreated` join `ActiveAnnotations`;
    3. the schemas, templates and configurations through `IViewImporter` with the same overlay;
    4. the record, unless a dry run or any `Failed` outcome.
  - `DataSetSeedOptions.cs` (`DryRun`, `Force`, `Again`, `AnnotationProgress`) and `DataSetSeedRequest.cs` (`Sets`, `Paths`, `Organization`, `Project`, `View`, `Owner`, `Options`).
- The result, `DataSetSeedResult.cs`: `Set`, `Hash`, `IsAlreadySeeded`, `FailedDependency`, `Documents` (a `ViewImportTally` over the files: `Summary` with the ignored files, `Summaries` per kind), `Lines` (`DataSetLinesResult.cs`: `Lines`, `GeneratorLines`, `Annotations`, their `Summary` from a `ViewImportTally` that keeps no items, `AncestorsCreated`, `RelationUpdates`), `Items`, `IsRecorded`, `Elapsed`, `IsSeeded`, `HasFailures`, `Failures`. **Decision:** `Items` (`DataSetSeedItem.cs`: `Path`, `Line` for `annotations.jsonl`, `Kind`, `AnnotationType`, `AnnotationKey`, `Outcome`, `ErrorCode`, `Message`, `Warnings`) lists every outcome but `Created` and `Unchanged`, files and lines alike: the items of a large set would otherwise be every created annotation; the counts are in the tallies.

### 3.8 Commands

Done by the next step: the `seed` and `sets` subcommands, the exit codes (`4`, `CommandParsingException`) and the printing of the results. `InitDbCommand` is unchanged; a `DataSetException` of the core seed still ends in its generic crash message until then.

### 3.9 The built-in sets

- `core` (`DbCreator/src/data-sets/core/`): `set.json` (target `42dotnet` / `main` / `default`, a description) and exactly what `Data.CreateCoreAnnotations` wrote: the annotation files `rst.storyteller`, `rst.supervisor`, `rst.scheduler`, `sbt.42`, `cnt.42.system`, `usg.42.{storyteller,supervisor,scheduler}`, `exe.42.{storyteller,supervisor,scheduler}.system` (each `{}`, as the old entities had no editable members) and `configurations/cnt.42.system.conf.json` (`{ "IsSystem": true, "Number": 42, "Text": "system" }`). The relation arrays are no longer written by hand: the creates maintain them.
- `CoreDbStructureBuilder` (constructor now with `DataSetSeeder`): `EnsureContainersAsync()` (new, public, returns `core`) holds the layout check (`DatabaseLayoutException` before any write, as before) and the containers; `BuildAsync()` calls it, seeds the access as before (`Data.SeedAccountAccessAsync`, out of scope), then seeds `CORE_SET` (`"core"`) through `SeedAsync`, prints one line (seeded with its counts, or seeded already) and returns the `DataSetRunResult` (it returned `Task` before; `await` callers are unchanged). So the core annotations get audit items, a second run reads the record and changes nothing, and a soft-deleted core annotation stays deleted.
- `Backend.CosmosDb/src/Data.cs`: `CreateCoreAnnotations` and its `UpsertAsync` are removed; `Create.cs` is deleted (a grep of the repository found no other user).
- `large`, `showcase` and the Playground manifest: later steps.

### 3.10 DI

- `DbCreatorEntryPoint.AddDbCreator(this IServiceCollection, IConfiguration)`: the options `BaseAccountOptions` and `StagingSeedOptions`, `TryAddSingleton<IFileSystem, FileSystem>`, `TryAddSingleton(TimeProvider.System)`, `AddViewImporter()`, and as singletons `DataSetCatalog`, `IDataSetSeedRecordStore` → `CosmosDataSetSeedRecordStore`, `DataSetProvisioner`, `DataSetSeeder`, `CoreDbStructureBuilder`, `GetBaseAnnotationsReview`.
- `Startup` keeps the logging, `LoggingOptions` and `IExtendedConsole`, and calls `AddDbCreator` after `AddCosmosDbAnnotations`; its own `IFileSystem` registration and the two options moved into it.
- `Backend.CosmosDb/test/Startup.cs` and `Access.Certificates/testint/CosmosFixture.cs` call `AddDbCreator(configuration)` instead of registering `CoreDbStructureBuilder`; the built-in sets reach their output through the project reference.

### 3.11 and 3.12

The admin UI's showcase set and the Aspire resource `db-seed` are later steps.

### Tests (4.1)

- `DbCreator/test/DbCreator.UnitTests.csproj` (`42.Platform.Storyteller.DbCreator.UnitTests`; xUnit v3 on MTP, `Microsoft.Testing.Extensions.TrxReport`, Shouldly, Moq, `TestableIO.System.IO.Abstractions.TestingHelpers`), listed in `42.mono.slnx` right after `DbCreator.csproj`. The DbCreator types are public, so no `InternalsVisibleTo`. 76 tests in `DataSets/`:
  - `DataSetManifestTests`: members in any case with comments, the defaults, an unknown member, invalid names, versions, types and JSON, a folder name that is not a set name, a missing manifest.
  - `DataSetAnnotationLinesReaderTests`: blank lines and comments, a line read as a file (members in any case, the target's project and view), numbers and dates as text, generators in every string at any depth, `$start` and the padding, literal lines, type codes in any case, thirteen kinds of failed lines with their numbers, failed generated annotations, the 200 000 limit before any expansion, the file of a folder with a BOM and CRLF.
  - `DataSetFolderTests`: the files in ordinal order without hidden paths, `__MACOSX` and skipped folders; the entries without the root `set.json` and `annotations.jsonl`; the documents of an export's folders with the ignored files; the hash equal for CRLF and LF, unchanged by skipped files and changed by any other change; the line-end normalization of text and binary files.
  - `DataSetContentTests`: duplicates of files and earlier lines.
  - `DataSetCatalogTests`: built-in sets (hidden and set-less folders left out, none without the folder), `--path` folders (one set or a folder of sets, relative paths), missing and empty folders, duplicate names, invalid manifests, dependency order, cycles, unknown names and dependencies.
  - `DataSetTargetTests` and `DataSetOwnerTests`: the given names win, agreeing and disagreeing manifests, the default view, missing names, the name rules (an existing organization is not checked), the base and other owners and invalid ids.
  - `BuiltInSetsTests`: with the real file system, the copied `core` set (target, the eleven annotations, the configuration, nothing ignored) and every built-in set reads without failures.
  - `DataSetSeederTests` (fakes of the importer, the bulk writer and the record store, Moq for the access and view services): the order of the writes (account, project, view) and the steps, one overlay, what is not sent and why (with lines and messages), the counts, no record after a failure; the record, a second run that reads nothing, a changed file and `Again`; a dry run that writes nothing and fills the overlay; an existing project used as it is; the refusal of an organization of another owner; a bad name before any write; dependencies first and no seed below a failed set; the sets of the folders without names.
- `Backend.CosmosDb/test/OrganizationsContainerStructureTests.cs`: `BuildAsync_Again_KeepsTheLayoutAndTheSeed` also checks that the second run reports `core` as seeded already and that `rst.storyteller` still has a single audit entry; the new `BuildAsync_SeedsTheSetCoreThroughTheServices` checks the relation arrays of `sbt.42`, `cnt.42.system` and `usg.42.storyteller`, the other keys, the System audit entry, the configuration and its stamp, and the record (hash, 12 documents, System stamp). No existing expectation had to change: none was about the quirks of the old upsert.
- Results:
  - `dotnet test --project src/Platform/Storyteller/DbCreator/test`: 76 of 76.
  - With `STORYTELLER_TESTS_USE_CONTAINER=true` (vNext), each class of `Backend.CosmosDb/test` in its own `--filter-class` run: `*OrganizationsContainerStructureTests` 6, `*CosmosAnnotationAuditTests` 7 (1 skipped, classic only), `*CosmosAnnotationServiceTests` 14, `*CosmosAnnotationConditionalWriteTests` 15, `*CosmosConfigurationInvalidationOrderTests` 5, `*CosmosConfigurationRecoveryTests` 38, `*CosmosViewTransferTests` 22, `*CosmosListingTests` 4, `*CosmosAnnotationPagingTests` 7, `*CosmosViewServiceTests` 9, `*TransferIsolationTests` 6, `*CosmosAccessServiceTests` 58, `*CosmosAnnotationBulkWriterTests` 18, `*CosmosViewImportTests` 32, `*CosmosAnnotationLifecycleTests` 23, all passed.
  - `dotnet test --project src/Platform/Storyteller/Access.Certificates/testint` (whole project, container): 73 of 73.
  - `dotnet run --project src/Platform/Storyteller/DbCreator/src -- --version` starts the host; `dotnet pack` puts `data-sets/core` into the tool package.
  - DbCreator and `DbCreator/test` build without new warnings (DbCreator keeps its one `SA1515` in `Program.cs`); the changed files of the two Cosmos test projects have no new warnings.

### Not in this phase

- 3.8 (commands, exit codes), the sets `large` and `showcase`, the Playground manifest, 3.11, 3.12, `DbCreator/testint` and its CI (4.2, 4.3), and the documentation of section 5 (`data-sets.md`, `CLAUDE.md`, the docs that mention DbCreator).
