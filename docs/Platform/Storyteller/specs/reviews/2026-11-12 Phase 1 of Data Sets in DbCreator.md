# Phase 1 of Data Sets in DbCreator

## Overview

Phase 1 of the spec [Data Sets in DbCreator](../2026-11-12%20Data%20Sets%20in%20DbCreator.md) (424-252) covers section 1, the reusable view importer in `Backend.Core/src/Transferring`: the passes of an import move out of the transfer job into a public `IViewImporter` / `ViewImporter`, the job's progress tally becomes the public `ViewImportTally`, `AddViewImporter()` registers the importer, and `ViewArchiveReader` reads loose entries (the files of a folder) besides a zip. The job keeps its constructor, its progress saves and its order of reads, and every existing test stays as it was. It is the commits `refactor(platform): read loose files with the view archive reader (424-252)` and `refactor(platform): extract the view importer from the transfer job (424-252)`, and `test(platform): cover the stop of an import that another worker took (424-252)` after the review. DbCreator does not use the importer yet (phase 3).

## What Was Done

### 1. `IViewImporter` / `ViewImporter`

- `IViewImporter.cs`: the interface of the spec, `ImportAsync(organization, project, view, files, options, overlay = null, cancellationToken = default)` returning `IAsyncEnumerable<ViewImportFileResult>`. Its comment adds two points the spec implies: nothing runs before the enumeration starts, and one call must not be enumerated twice at once (the overlay is not thread-safe).
- `ViewImportFileResult.cs`: `public sealed record class ViewImportFileResult(ViewArchiveFile File, ViewImportResult Result)`, in its own file (StyleCop `SA1402`).
- `ViewImporter.cs`: the members of `ViewTransferService` moved unchanged in behaviour: `OrderByPass`, `IsOverlayCandidate`, `ReadStoredAsync` (now with organization, project and view instead of the record), `ReadConfigurationKeysAsync`, `FillOverlay`, `Track`, `GetSchemaKey`, `ImportFileOrConflictAsync`, `IsWriteConflict`, `ImportFileAsync` (both with organization, project and view) and the record `StoredDocuments`. `ImportAsync` is an async iterator (`[EnumeratorCancellation]`): it orders the files, makes the pre-reads, then per file checks the token, fills the overlay before the first file that is not an annotation, imports, tracks and yields. A given overlay is used and filled; without one, the iterator makes its own when it starts, so every enumeration of a call without an overlay starts empty.
- Constructors: the public one of the spec (`IAnnotationService`, `IConfigurationSchemaService`, `IConfigurationTemplateService`, `IConfigurationService`, `ILogger<ViewImporter>`), and an internal one with an `ILogger` for the job. The container sees only the public one.
- The conflict warning reads `The file {Path} of the import into {Organization}.{Project}.{View} failed on concurrent changes.`: it names the path and the view instead of the job id, as the spec says. No log scope with the job id was added.
- `ViewImportOverlay.cs`: its comment names the importer instead of the transfer service, and says that sequential calls may share one overlay.

### 2. `ViewImportTally`

- `ViewImportTally.cs`: `ImportProgress` without the job, with `Add(file, result)`, `Total`, `Processed`, `Summary` (with `ExcludedFiles` and `IgnoredFiles`), `Summaries` (one per included kind, in import order), `Items` (a copy) and `AreItemsTruncated`, and the same counting rules: every result counts in the total and in its kind; a result with warnings counts once in `Warnings` unless it is `Unchanged`; every outcome but `Unchanged` is an item, up to the limit, after which the items are marked truncated.
- **Refinement:** the constructor is `(IEnumerable<ViewArchiveContent>? include, int total, int ignoredFiles = 0, int excludedFiles = 0, int maxItems = int.MaxValue)`. The include is resolved with `ViewArchiveInclude.Resolve` (null or empty means all four kinds, distinct and in import order), and the reader's counts and the limit are optional, so a caller without a reader or a limit (DbCreator) needs only the kinds and the total. The job passes all five as before.
- The job keeps `Truncate(result)` before `Add` and gets a private `Apply(ViewImport, ViewImportTally)` that writes `TotalCount`, `ProcessedCount`, `Summary`, `Summaries`, `Items` and `AreItemsTruncated` to the record.

### 3. `ViewTransferService`

- The 10-argument constructor is unchanged; it builds the importer from the same four services and its own logger (`new ViewImporter(..., logger)` through the internal constructor) and keeps it as a `ViewImporter` field, so the three hand-made constructions in the tests (`ViewTransferServiceTests`, `CosmosViewTransferTests`, `TransferIsolationTests`) stay as they are.
- `ImportAsync` reads the archive as before, saves the first progress (`ProcessedCount = 0`), builds the options, and then runs `await foreach (var (file, result) in _importer.ImportAsync(...))` with the same save cadence (every 50 files or 2 s). The importer is lazy, so its pre-reads still follow the first save; a lost job (`SaveImportAsync` returning null) returns from inside the loop, which disposes the enumerator and stops the import; the final save and the deletion of the upload are unchanged.
- The private members listed in section 1 and the nested `ImportProgress` are gone from the service.

### 4. `AddViewImporter()`

- `ViewTransferEntryPoint.AddViewImporter(this IServiceCollection)`: `TryAddSingleton<IViewImporter, ViewImporter>()`; it needs the services of the four kinds (Cosmos registers them as singletons in `AddCosmosDbAnnotations`). `AddViewTransfers` calls it. DbCreator calls it in phase 3 (`AddDbCreator`).
- `Api.Functions` (the only caller of `AddViewTransfers`) builds unchanged.

### 5. Reading loose files

- `ViewArchiveEntry.cs`: `public sealed record class ViewArchiveEntry(string Path, long Length, Func<Stream> Open)`; the path may use `/` or `\`.
- `ViewArchiveReader.Read(IReadOnlyCollection<ViewArchiveEntry> entries, ViewTransferOptions options, IEnumerable<ViewArchiveContent>? include = null)` holds the reading: `Inspect` takes the entries (the count against `MaxArchiveEntries`, the separators normalized, the declared lengths against `MaxExtractedBytes`) and `ReadFile` opens an entry through its `Open`. `Read(Stream, ...)` and `Validate(Stream, ...)` map the zip's entries to `ViewArchiveEntry` (`entry.FullName`, `entry.Length`, `entry.Open`) and run the same code inside their `using` of the zip, because the entries open lazily. Backend.Core keeps no file-system dependency.
- **Addition:** an `IOException` or `UnauthorizedAccessException` of `Open` or of the read fails the file with `InvalidDocument` and `The file cannot be read: …` (a loose file can vanish or be locked between the listing and the read); `InvalidDataException` keeps its `cannot be decompressed` message.
- The zip overloads now build the list of entries before the count check; `ZipArchive.Entries` had already read the whole central directory, so the check costs the same.
- The class comment says "never opened" instead of "never decompressed" for the excluded kinds.

### 6. Tests

- `Backend.Core/test/Transferring/ViewImporterTests.cs` (new, on the existing fakes and `ImportLog`; files from the new entry reader and made in memory):
  - `Import_TakesTheFilesInAnyOrderInThePassesOfAnImport`: forward and reversed input with a file made in memory give the same calls and the same outcomes in the order of the passes.
  - `Import_ReadsEachKindOnceBeforeTheAnnotationPassAndNothingBeforeTheEnumeration`: no read and no call before the enumeration; with `Skip`, one existence read, one schema-key read and the paged listing, all before the first annotation import; stored documents stay out of the overlay.
  - `Import_FailsTheFilesWithAnErrorWithoutACallAndTracksEachOutcomeBeforeItComes`: a read error, a duplicate and a file made in memory with an `ErrorCode` fail without a call; the overlay holds each failed annotation when its outcome comes, a duplicate changes nothing.
  - `Import_SharesAGivenOverlayOverSequentialCalls`: a dry run of annotations, then the schema and configurations with the same overlay: the second call sees what the first made active or failed and fills the given overlay; a call without one starts empty.
  - `Import_StopsWithTheEnumeration`: a `break` after the first outcome and a token cancelled after it make no further call.
  - `Import_AWriteConflictFailsItsFileOnly` and `Import_AnyOtherExceptionEndsTheEnumeration`.
  - `Import_PassesTheOptionsToEveryKind`, `AddViewImporter_RegistersOneImporterOverTheServicesOfTheFourKinds` (resolved with `ValidateOnBuild` and `ValidateScopes`), `AddViewTransfers_AddsTheImporter`.
- `ViewImportTallyTests.cs` (new): the counting rules in total and per kind, the summaries in import order of the included kinds, the items' limit and copy.
- `ViewArchiveReaderTests.cs`, five tests of the entry overload: the same files, errors, ignored count and annotation parsing as the same zip (with `set.json` and `annotations.jsonl` among the ignored files); backslashes and hidden paths; only the documents read are opened (not the excluded, ignored, skipped, misnamed, duplicate or declared-too-large ones); the limits of a zip, with a length that lies; a file that cannot be opened. `TransferTestData` gets `Entries(...)` and `Entry(...)`.
- The existing job tests (`ViewTransferServiceTests`) and the Cosmos tests are unchanged. One job test was added after the review: `ProcessImport_StopsWhenAnotherWorkerTakesTheJob` (60 configuration files; during the 50th import another worker takes the job, so the progress save after it fails): exactly 50 files are imported, the job stays `Running` with the other worker's record (`ProcessedCount` 0), the upload is kept, and the importer's single existence read comes after the `ProcessedCount = 0` save. For it, the fake `InMemoryTransferStore` gets a `Saved` callback and `TakeOver(id)`, which gives the stored job a new ETag.
- Results: `dotnet test --project src/Platform/Storyteller/Backend.Core/test` passed 1386 of 1386 (1367 before, 19 new). With `STORYTELLER_TESTS_USE_CONTAINER=true` (vNext), `Backend.CosmosDb/test` with `--filter-class "*CosmosViewTransferTests"` passed 22, `*TransferIsolationTests` 6 and `*CosmosViewImportTests` 32. `Backend.Core`, its tests and `Api.Functions` build without new warnings.

### Review findings

- **Fixed (test coverage):** no job-level test covered the stop of an import whose job another worker took, although the refactor changed how the stop works (a `return` from inside `await foreach`, which disposes the importer's iterator, instead of a `return` from a plain loop), and the order of the first progress save and the pre-reads was checked only through the importer's laziness. The behaviour was right; `ProcessImport_StopsWhenAnotherWorkerTakesTheJob` now pins both. A job that collected the outcomes eagerly before the loop fails it (checked by hand).
- Nothing was rejected.

### Not in this phase

- The documentation of the importer and the entry reader in `docs/Platform/Storyteller/export-import.md` belongs to section 5 of the spec.
