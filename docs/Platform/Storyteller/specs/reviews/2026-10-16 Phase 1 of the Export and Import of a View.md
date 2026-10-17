# Phase 1 of the Export and Import of a View

## Overview

Phase 1 covers the backend (sections 1.1-1.16) of [Export and Import of a View](../2026-10-15%20Export%20and%20Import%20of%20a%20View.md), Linear 424-40. An archive now holds the four kinds of documents of a view: annotations, schemas, templates and configurations. The file name tells the kind (`ViewArchiveNames`), the folder format writes one folder per kind with manifest v2, and the git format writes one time-ordered history of every kind in nested trees. The export reads the included kinds in reverse dependency order. The import runs the passes annotations, schemas, templates, configurations with the options `Include`, `Existing`, `Force` and `DryRun`, and an overlay lets a schema and the configurations it validates be imported together. The types of PR #88 are renamed to `View*` names; the storage names are unchanged. The routes, the OpenAPI document and the SDKs (Phase 2), the CLI (Phase 3) and the admin UI (Phase 4) are not part of it: `Api.Functions` keeps the routes of PR #88 and passes the defaults (all four kinds, `Overwrite`).

## What Was Done

### 1.1 Public model (`Abstractions.Annotations/src/Model/ViewTransfer.cs`)

- `ConfigurationTransfer.cs` is replaced by `ViewTransfer.cs`: the eight renamed types, the enums `ViewArchiveContent`, `ViewDocumentKind` and `ViewImportExisting`, and the new members of the spec:
  - `ViewExportCreate.Include`;
  - `ViewExport.Include`, `AnnotationCount`, `SchemaCount`, `TemplateCount`, and `VersionCount` with its new meaning;
  - `ViewImport.Include`, `Existing` and `Summaries`;
  - `ViewImportSummary.Content`, `Warnings` and `ExcludedFiles`;
  - `ViewImportItem.Kind` (required), `AnnotationType` and `Warnings`.
- **Deviation:** `ViewImportWarning` is in its own file, `ViewImportWarning.cs`. A sixth record in `ViewTransfer.cs` would have added a StyleCop SA1402 warning; the file keeps the warnings `ConfigurationTransfer.cs` had.
- The count of a kind that the export left out is null, not 0. `VersionCount` is always set.
- Internal renames in `Backend.Core/src/Transferring`: `ViewTransferService`, `IViewTransferService`, `ViewArchiveReader`, `ViewArchiveWriter`, `ViewArchiveFile`, `ViewArchiveSource`, `ViewArchiveStatistics`, `ViewImportResult` (gains optional `Warnings`), `ViewTransferKind`, `ViewTransferMessage`, `ViewTransferNames`, `ViewTransferOptions`, `ViewTransferRecord`, `ViewTransferEntryPoint` (`AddViewTransfers`), `IViewArchiveStorage`, `IViewTransferQueue` and `IViewTransferStore`. Beyond the spec's list:
  - `ConfigurationArchiveContent` (the files of an uploaded archive) is `ViewArchiveDocuments`, because `ViewArchiveContent` is now the public enum of kinds;
  - `ConfigurationDocumentJson` is `ViewDocumentJson`, because it serializes every kind;
  - `ConfigurationDocument` and `ConfigurationHistory` keep their names: they are configuration-shaped, and 1.6 reuses `ConfigurationDocument`.
- Other projects: `CosmosViewTransferStore` and `ViewTransferEntity` (Cosmos DB); `AzureBlobViewArchiveStorage`, `AzureQueueViewTransferQueue` and `AddAzureStorageViewTransfers` (Azure Storage).
- `Api.Functions` keeps its class, function and route names (`ConfigurationTransferHttp`, `ConfigurationTransferFunctions`); only the type references and the DI calls changed, because Phase 2 replaces them. `OpenApiDocumentTests` expects the `View*` schema names, which the live document now publishes. The committed `open.api.v0.9.15.json`, `Sdk.NSwag` and the TypeScript SDK keep the `Configuration*` names until Phase 2.
- The storage names stay: the queue and container `configuration-transfers`, the partition `{project}.transfer`, the options section `ConfigurationTransfers`.

### 1.2 Error codes

`ErrorCodes` has `DocumentExists`, `DependencyFailed` and `InvalidSchema` (outcomes of files) and `AncestorCreated` and `AnnotationRecreated` (warnings). There was no `UnknownAnnotationType` to drop.

### 1.3 File names (`ViewArchiveNames`, `ViewArchiveName`)

- One builder per kind, with versions (`GetAnnotationFileName`, `GetConfigurationFileName`, `GetTemplateFileName`, `GetTypeSchemaFileName`, `GetAnnotationSchemaFileName`, `GetDescendantTypeSchemaFileName`), a generic `GetFileName(kind, type, key, version)`, `GetPath` (`<folder>/<file name>`), `GetFolder` and `GetContent`. The builders encode the reserved second segment (`rst.schema` is `rst.%73chema.json`).
- `Describe(kind, type, key)` words a document as the 1.8 table does (`annotation rst.payments`, `type schema exe`, `exe schema below sbt.northwind`, `schema of rst.payments`, `template exe`, `configuration of rst.payments`). Git messages, duplicate messages and `DocumentExists` messages use it.
- `TryParse` follows rules 1-5 and returns a `ViewArchiveName`: `Kind`, `AnnotationType`, `AnnotationKey`, `Version`, `Content`, `Identity`, and `ErrorCode`/`Message`. `CanDescend` is built from `AnnotationHierarchy.GetAncestorSchemaSources`; a descendant type that fails it is a document carrying `InvalidKey`.
- `IsSkippedPath`: any segment that starts with `.`, and a leading `__MACOSX`. **Refinement:** a bare `.` segment (a `./` prefix) is the current folder, not a hidden one.
- `ConfigurationFileNames` and its tests are gone; `GetArchiveFileName` moved here.

### 1.4 Archive layout and manifest

- The zip name `export-{organization}-{project}-{view}[-git]-{yyyyMMdd-HHmmss}Z.zip` comes from `ViewArchiveNames.GetArchiveFileName`.
- One folder per included kind that has documents; version files sit next to the current file. A history without versions writes nothing. Formatting is the one of PR #88.
- `export.json` is manifest v2 with exactly the specified members, in the specified order. The v1 `Format` member is gone. **Refinements:**
  - `Annotations`, `Schemas`, `Templates` and `Configurations` are always present; an excluded kind is an empty array. `Include` is the resolved list (all four for null or empty).
  - Order: annotations by key; schemas by kind (type, descendant type, annotation), then type, then key; templates by type; configurations by key.
  - A deleted template, schema or configuration has `Author` and `UpdatedAt` null as well as `File` and `Version`.

### 1.5 Annotation documents (`ViewAnnotationDocument`)

- `Write`: the editable members in the API's order and PascalCase names, null members left out, `UnitType` and `UnitDefinition` for units only.
- `Parse` (reworked in the review, see [Review](#review)): a `Utf8JsonReader` with comments and trailing commas allowed, depth 64 (`ViewDocumentJson.MaxDepth`), duplicate properties and anything after the object rejected. It builds Newtonsoft tokens with date-like strings kept as strings and numbers as `JRaw` of their text. Errors are a Newtonsoft `JsonReaderException`, as for the other kinds.
- `TryRead(JObject, FullKey, …)`:
  - Member names match in any case, as in the API's body reader. Two spellings of one member (`Title`, `title`) fail with `InvalidDocument`.
  - The ignored server-owned members are those of the key's runtime model that are not editable, so the `GET` body of an annotation can be saved as its file. The relation members of other types (`UnitNames` on a unit) are unknown, like `UnitType` on a type other than a unit.
  - `AnnotationKey` is compared with the type code in any case and the names exactly; a null or non-string value fails with `InvalidDocument`.
  - One unknown member gives `Unknown member 'X'.`; several are listed in one message, `Unknown members 'a', 'b'.`.
  - Values are typed through System.Text.Json, as the API types them; a wrong type fails with `InvalidDocument`. Missing members are cleared, a unit gets the create defaults (`event`, `unknown`), `{}` is valid, and `AnnotationRules` failures give `InvalidAnnotation`.
- **Deviation:** `TryRead` takes a `JObject`, to fit `ImportAnnotationAsync(FullKey, JObject, …)`. Dates and numbers therefore keep their text only when the file was parsed with `ViewAnnotationDocument.Parse`, which the reader does for annotation files.
- **Change beyond the transfer (review):** `AnnotationAuditDiff` treats two numbers as equal when they are the same finite double and one of them has a fraction or an exponent. Cosmos DB answers such a number with 17 digits in a point read and with the fewest digits in a query. Integers still compare exactly. A `SetAnnotationAsync` that only changes the text of such a number now writes nothing.

### 1.6 Documents of a view

- New reads: `IAnnotationService.GetViewAnnotationsAsync` (`IReadOnlyList<Annotation>`, with the stamps), `IConfigurationTemplateService.GetTemplateHistoriesAsync` (`TemplateHistory`: `AnnotationType`, `Versions`, `Current`) and `IConfigurationSchemaService.GetSchemaHistoriesAsync` (`SchemaHistory`: `Kind`, `AnnotationType`, `AnnotationKey`, `Versions`, `Current`).
- **Deviation (review):** `CosmosAnnotationService.GetViewAnnotationsAsync` does not page through `GetAnnotationsAsync`. That LINQ query reads with the SDK's Newtonsoft serializer, which turned `Values` numbers into doubles and date strings into local `DateTime`s, so an exported annotation did not compare equal (1.5). It pages one System.Text.Json stream query through `AnnotationItemStore.QueryPageAsync`, as `GetDeletedAnnotationsAsync` does: the items of the view's project partitions that have an `AnnotationType` and no `Deletion`.
- **Deviation:** "active only" is read as "not deleted". Disabled, scheduled and expired annotations are exported; otherwise their configurations, which the export holds, would be skipped with `AnnotationNotFound` on a fresh view.
- Templates: one query on `{project}.template` for `{view}.gen.*` and `{view}.gnv.*`. The type code comes from the id, and only valid lower-case codes are taken. Results are ordered by type.
- Schemas: one query on `{project}.schema` for `{view}.cfs.*` and `{view}.csv.*`. The identity is parsed by `ParseSuffix`, which `ToSchemaSummary` now shares. Results are ordered by kind, type and key.
  - **Deviation:** the schemas of annotations that are not active are found with one ReadMany of the annotation items the schemas name (new `AnnotationItemStore.ReadStatesAsync`), which tells active, deleted and missing apart in one request. The spec names `ReadDeletedAnnotationKeysAsync` plus a separate existence check; the result is the same. A disabled annotation's schemas are kept.
- **Deviation:** an empty document is not a version for schemas too, not only for templates. An empty current document gives `Current = null`, and a document without any version with content is left out.
- **Deviation:** times and authors come from the entities' `Modified` stamp (`At`; `By.Name ?? By.Id`), as `GetConfigurationHistoriesAsync` does. The spec's `CreationTime` and `_ts` describe older entity shapes.
- `ConfigurationDocument` gains an optional `AuditActor? Actor`, which all three reads fill and the git format signs with (1.8). The manifest's `Author` is unchanged.
- The new `Configuring/DocumentHistories<TIdentity>` groups current documents and versions for all three reads; `GetConfigurationHistoriesAsync` uses it with unchanged behaviour. `ConfigurationArchiveRow` is now `DocumentArchiveRow`. Ids whose type code or key do not parse to the canonical form are skipped, because no file name can be written for them.
- `ViewArchiveSource` gains `Include` (nullable), `Annotations` (`IReadOnlyList<Annotation>`, no new type), `Schemas` and `Templates`. **Deviation:** `Histories` is renamed `Configurations`, since schemas and templates are histories too.
- New `ViewArchiveInclude`: `All`, and `Resolve`, which returns the kinds distinct, in import order, all four for null or empty.
- `ViewTransferService` reads the included kinds in the order configurations, templates, schemas, annotations, and reads nothing for an excluded kind.

### 1.7 Folder format (`ViewArchiveWriter`)

- The included kinds are written at `ViewArchiveNames.GetPath`, annotation files through `ViewAnnotationDocument.Write`.
- Entry times: a version file has its version's time, an annotation its `Modified.At`, `export.json` the export time. **Refinement:** an annotation without `Modified` falls back to `Created.At`, then to the export time.
- `ViewArchiveStatistics` has `AnnotationCount`, `SchemaCount`, `TemplateCount`, `ConfigurationCount` and `VersionCount`, computed by one `GetStatistics` for both formats.
- The writer does not filter annotations with a `Deletion`; it relies on `GetViewAnnotationsAsync`.

### 1.8 Git format (`Git/GitRepositoryWriter`, `ViewArchiveWriter.WriteGit`)

- **Nested trees:** a private `TreeNode` per directory. Tree entries are ordered like git through `GitPathComparer.GetTreeEntryName(name, isTree)` (a subtree compares as `name/`), with modes `100644` and `40000`. Only the directories on a changed path are written again, each as a delta against its previous version; an unchanged directory keeps its tree id. A removal drops the directories it leaves empty.
- `GitIndexWriter` needed no code change: it already sorted full paths in byte order.
- **Addition:** paths are checked before anything is packed. Empty, `.`, `..`, `.git` and NUL segments throw `ArgumentException`; a file and a directory of the same name throw `InvalidOperationException`. Both are guards for `git fsck --strict`.
- **One history:** every included kind is written at the same paths as in the folder format. The documents are listed in import order, and a priority queue merges them by (time, position of the document), with one pending commit per document. Each document's versions therefore stay in order even when their times are equal or go backwards.
  - **Deviation:** "then name" within one time is read as the import order of 1.15, as the spec's Order section says. Annotations go by graph depth, type and key (ancestors first, where a name order would put `exe.*` before `sbt.*`); schemas go type, descendant type, annotation, then by type and key. A version number is never compared across documents.
- **Annotations:** one commit each, with the current content, at the `Created` stamp, by its actor as author and committer. **Refinement:** without `Created`, `Modified` is used, then the export time with the system signature.
- **Signatures:** `GitSignature.FromActor(AuditActor?, when)` gives `account: <id>`, `machine: <id>` or the system signature. Version commits use `FromActor(version.Actor)` and fall back to `FromAuthor(Author)`, so one person has one git identity in annotation and version commits.
- **Messages** come from `Describe` and match the 1.8 table. The last commit, `Remove deleted documents`, is at the export time by the system signature; there is none when nothing was deleted. **Refinement:** its body lists the paths in document order (schemas, templates, configurations), not in byte order.
- The working-tree and index times are those of the commit that last wrote a file, as in PR #88, so an annotation has its `Created.At` there. The `Modified.At` rule of 1.7 applies to the folder format only.
- The `VersionOrder` comparer and the messages of PR #88 are gone.

### 1.9 Reading an archive (`ViewArchiveReader`)

- `Read(stream, options, include = null)` skips hidden paths and `__MACOSX` without counting them and parses only the file name. Included kinds become `ViewArchiveFile`s (`Path`, `Kind`, `AnnotationType`, `AnnotationKey`, `Content`, `ErrorCode`, `Message`). Excluded kinds are counted without being decompressed. Version files, `export.json` and other names count as ignored.
- **Refinement:** version files of an excluded kind count as ignored, not excluded: the import never reads a version file, whatever the include.
- Duplicates are found by `ViewArchiveName.Identity`, across folders, type-code case and key encodings. The first path in ordinal order wins; the others get `DuplicateFile` with `The file 'X' already holds the <document>.` (`Describe`).
- A name carrying `InvalidKey` becomes a failed file without being decompressed. Its declared size still counts toward `MaxExtractedBytes`, and duplicates are checked first.
- Annotation files are parsed with `ViewAnnotationDocument.Parse`, the other kinds with `ViewDocumentJson.Parse`.
- `MaxExtractedBytes` counts all included files together; `MaxDocumentBytes` applies to every kind.
- `Validate(stream, options)` keeps its signature and counts every kind at upload, so `Read` can never fail a limit that `Validate` passed.

### 1.10 Import options and the overlay

- `ViewImportOptions`: `Actor`, `Existing`, `Force`, `DryRun`. **Deviation:** there is no `Author`. Every write path now stamps versions and audit entries with `AuditStamp.Of(actor)`, so an author string would be unused.
- `ViewImportOverlay` has the read-only members of the spec. **Deviation:** it also has public mutators, `AddActiveAnnotation(key, isRecreated)`, `AddFailedAnnotation`, `SetSchema`/`RemoveSchema` and `SetConfiguration`/`RemoveConfiguration`, because `Backend.CosmosDb` writes into it and `Backend.Core` has no `InternalsVisibleTo`. The mutators take an `AnnotationKey`, so the stored keys are always canonical. The overlay is not thread-safe.
- New `SchemaKey`, a record struct of (`Kind`, `AnnotationType`, `AnnotationKey`) with `ForType`, `ForAnnotation` and `ForDescendantType`. New `ViewSchemaTarget` as specified.
- The service fills the overlay (1.15). It does not check that a schema is valid, because `Backend.Core` has no NJsonSchema; the schema service ignores overlay layers that are empty or not valid JSON Schemas (1.12).

### 1.11 Importing an annotation (`CosmosAnnotationService.Import.cs`)

- As specified. `UpdateCoreAsync` and `CreateCoreAsync` are reused unchanged, with their audit entries by `options.Actor`.
- The outcome and the warnings are computed from the states read before the write (the key's node and one `ReadStatesAsync` of its ancestors), in real and dry runs alike, so the dry run predicts the real run. An ancestor in `overlay.ActiveAnnotations` counts as active.
- The step runs inside the existing `RetryAsync`: a key created meanwhile runs it again on the update path. A `ConflictException` after the last retry is not caught and fails the job.
- **Deviation:** the active and re-created keys are recorded in the overlay in real runs too, which keeps it "the view as the import leaves it". The method does not record its own failures; the service does (1.15).
- With `Skip`, `AnnotationDeleted` names every deleted key on the path, ancestors first.
- Messages are in the present tense, so they read the same in a dry run, for example `The missing ancestors rst.payments, usg.northwind.payments are created with defaults.` and `The ancestor sbt.x failed in this import.`.
- New `IAnnotationService.FindActiveAnnotationKeysAsync` for 1.15: one `ReadStatesAsync` (ReadMany) of the given keys.

### 1.12 Importing a schema (`CosmosConfigurationSchemaService.Import.cs`)

- The checks, in order:
  1. A descendant-type schema whose type cannot descend from its key fails with `InvalidKey` (`CanDescend`), with the reader's message. The reader already fails such a file, so this guards direct calls.
  2. The annotation must be in `overlay.ActiveAnnotations` or stored active (`AnnotationItemStore.ReadStateAsync`). Otherwise the file is skipped with `DependencyFailed`, `AnnotationDeleted` or `AnnotationNotFound`. **Refinement:** `DependencyFailed` applies only when the annotation is not active; when it is both deleted and failed, `DependencyFailed` wins.
  3. `{}` is `Skipped` with `EmptyDocument`; a document that is not a valid JSON Schema is `Failed` with `InvalidSchema` (`Invalid JSON Schema: …`), even with `force`.
  4. An equal stored schema is `Unchanged`; with `Skip`, a different one is `Skipped` with `DocumentExists`. **Refinement:** a stored `{}` counts as no document, as in the template import and the export.
  5. Compliance, then the dry run or the write.
- `CollectComplianceErrorsAsync` takes the overlay:
  - an overlay document replaces the stored one, and the overlay's documents of keys without a stored document are checked too;
  - the stored document of a key in `RecreatedAnnotations` is not checked;
  - each layer is the schema being imported, else the overlay's layer, else the stored one.
- **Refinements of the compliance check:**
  - The directives of overlay documents (`$remove`, `$patch`) are resolved on a copy. A document whose directives fail, or that ends empty, will not be written, so the stored document is checked instead (none for a re-created key).
  - Stored configurations of deleted annotations that the import does not re-create are still checked, as `SetSchemaAsync` checks them.
- A violation is `Failed` with `SchemaViolation`, listing at most 20 keys in ordinal order (`… and N more`). With `force` the schema is written with the warning `SchemaViolation` and logged through the shared `LogForcedViolations`, on a real write only.
- A dry run returns `Created` or `Updated`. Otherwise `SaveAsync(…, isChecked: true)` writes without repeating the validity or the compliance check.
- Refactoring: `ResolveCombinedAsync` walks a list of `SchemaIdentity` values, and the new `ResolveLayerAsync` picks the override, the overlay or the stored layer. `SchemaOverride` holds the `SchemaIdentity`. `SchemaIdentity.ToSchemaKey()` and a private `OverlaySchemaLayers` (each layer checked once) are new. `ValidateContentAsync` gains an optional `ViewImportOverlay? overlay` for the dry run of configurations.
- New `GetSchemaKeysAsync` for 1.15: one single-partition query for the canonical keys of the non-empty current schemas.
- A `SchemaConcurrencyException` after the last retry is not caught and fails the job.

### 1.13 Importing a template (`CosmosConfigurationTemplateService.ImportTemplateAsync`)

- As specified. `CreateOrUpdateTemplateAsync` and the import share the private `WriteTemplateAsync(…, merge)`; the import replaces (`merge: false`). The version comes from `LastVersion`, and the effective documents are invalidated as on any template write.
- **Refinement:** a broken directive gives the exception's code, `PatchInvalid` or `PatchTestFailed` for a failed `test` operation, as the configuration import of PR #88 does.
- **Refinement:** a stored `{}` counts as no template, so the outcome is `Created` and `Skip` overwrites it.
- **Refinement:** an `Unchanged` real run completes a pending cache invalidation, as `CreateOrUpdateTemplateAsync` does when nothing changes. A retried import whose first run stopped before the invalidation still invalidates.

### 1.14 Importing a configuration (`CosmosConfigurationService.ImportConfigurationAsync`)

- New signature `(FullKey, JObject, ViewImportOptions, ViewImportOverlay)` with the three changes of the spec. `DependencyFailed` applies only when the annotation is not active, as for schemas: a stored active annotation whose file failed keeps its configuration importable, in line with decision 8.
- The overlay counts in real runs too, for the annotation check and for `RecreatedAnnotations`. `WriteConfigurationAsync` still refuses a deleted annotation if the state changed meanwhile.
- **Change from PR #88:** validation always runs when a schema service is present, including with `force`, so a forced write can carry the warning `SchemaViolation` (`Force stores the document although it violates its schema: …`). A dry run validates against the overlay's layers (`ValidateContentAsync(…, overlay)`); a real run against the stored schemas.
- Directives apply to a copy, so the caller's `JObject` is no longer changed.

### 1.15 Processing (`ViewTransferService`)

- **Jobs:** `StartExportAsync` gains `include`, and `StartImportAsync` gains `include` and `existing` (positional parameters). The jobs store `Include` resolved, so an export without an include reports all four kinds instead of null; `ViewTransferEntity` keeps the whole model, and the queue message stays identity-only. `Api.Functions` passes `include: null` and `ViewImportExisting.Overwrite` until Phase 2.
- **Export:** the read order and the include of 1.6; the counts of 1.1.
- **Import:**
  - The reader gets the job's resolved include.
  - `OrderByPass` sorts the files into the passes of the Order section. The path is the last key, so a duplicate follows the file that holds its document. Files that failed while reading are reported in their kind's pass.
  - `ReadStoredAsync` reads what the overlay needs before the annotation pass, with at most one read per kind. **Deviation from "one query per kind":**
    - annotations: `FindActiveAnnotationKeysAsync` over the keys of the schema and configuration files;
    - schemas, with `Skip` only: `GetSchemaKeysAsync`, because `ListSchemasAsync` cannot tell empty documents apart;
    - configurations, with `Skip` only: the paged `ListConfigurationsAsync`, keeping keys with `HasContent`;
    - templates: no read, since they are not in the overlay and `ImportTemplateAsync` reads its own state.
  - One `ViewImportOptions` (`Actor = CreatedBy`, `Existing`, `Force`, `DryRun`) and one `ViewImportOverlay` per job.
  - `FillOverlay` runs once, before the first file after the annotation pass. It takes the schema and configuration files that were read without an error and are not empty, and are a type schema or have an annotation that is stored active or in `overlay.ActiveAnnotations`. With `Skip`, a document the view already has is left out, except the configuration of a re-created annotation. **Deviation:** 1.10 asks for an annotation only for configurations; annotation and descendant-type schemas need one too, because `ImportSchemaAsync` skips them otherwise.
  - `Track` follows each outcome. **Refinements:**
    - A `Failed` annotation, or one `Skipped` with `DependencyFailed`, goes to `FailedAnnotations`. `DocumentExists` and `AnnotationDeleted` do not mark the key.
    - A `Failed` or `Skipped` schema or configuration leaves the overlay.
    - A `DuplicateFile` changes nothing, because its document is the first file's, which may have succeeded.
- **Reporting:** items in pass order with `Kind`, `AnnotationType` and `AnnotationKey`. `Summaries` has one summary per included kind, in import order, and the total carries `ExcludedFiles` and `IgnoredFiles`. Warnings are counted per kind and in total. **Refinement:** warning messages are cut at 500 characters, like item messages.
- **Retries:** a re-run reports the documents written before as `Unchanged`. A cut-off job left `Running` (Backend.Core) and a second import of the same archive (Cosmos DB) cover this.
- Per-file exceptions are not caught, as before: `ConflictException` and `SchemaConcurrencyException` after their retries fail the whole job.

### 1.16 Tests (Phase 1)

Every case of the spec's list is covered. Beyond it:
- **Git:** two `git` CLI tests. One runs on an export with every kind: `fsck --full --strict --no-dangling`, `status --porcelain`, `ls-files`, `ls-tree`, the full `log`, `log -- templates/exe.template.json`, the remove commit's body, and `show` of current and removed files. The other checks the tree order of a hand-built repository. `GitTestReader` walks subtrees and checks the git order, unique names, no empty subtrees and the object type of each mode.
- **End to end:** `CosmosViewTransferTests` runs `ViewTransferService` over the Cosmos services and store with in-memory archives and queue:
  - a folder export of every kind into another view: the dry run predicts the real run, the copy equals the source, and a second import is all `Unchanged`;
  - the working tree of a git export with an include filter;
  - the text of numbers and dates of `Values` (review);
  - a broken `$remove` reported on its file (review).
- **Mutation checks:** the tests were run against deliberately broken code to check that they fail. The breaks were the git tree-entry order, the overlay's documents, layers and `RecreatedAnnotations` in the schema check, and in the service the `Skip` rule, the re-created rule, the annotation check, schema removal, the duplicate guard, the `DependencyFailed` rule and the depth order. Each break failed at least one test.
- Not covered: paging of `GetViewAnnotationsAsync` beyond one page (1000 annotations), which is slow on the emulator.

### Documentation

- `configuration-transfer.md` is renamed `export-import.md` and rewritten from the code. It covers what travels, the file names and the reserved segment, folders and hidden paths, both formats with manifest v2 and the git messages, the import passes, the overlay, the outcomes and warnings, the dry run, retries, the limits of large imports, the per-file errors that still fail the job, and the jobs.
- Its API, CLI and admin UI sections describe this branch as it is today, the routes, scopes and commands of PR #88, and say what each later phase replaces.
- `annotation-lifecycle.md`: an import with `Overwrite` is the fourth way to re-create a deleted annotation; the export ignores the active status; the audit entries an import writes.
- `templating.md`: `GetTemplateHistoriesAsync`, and `ImportTemplateAsync`, which replaces instead of merging, shares the write path and invalidates the cache.
- `definitions.md` (not in the spec): how an import checks schemas against the configurations as the import leaves them.
- The comments of `ConfigurationTransferHttp.cs` and `ui.admin/src/app/mock/transfer-handlers.ts` that named the old document now name `export-import.md`.

## Tests

| Project | Result |
| --- | --- |
| `Backend.Core/test` | 425 pass. New or rewritten test classes in `Transferring/` (test methods, theories counted once): `ViewArchiveNamesTests` 30, `ViewAnnotationDocumentTests` 25, `GitRepositoryTests` 25 (including both `git` CLI tests, run with git 2.49), `ViewTransferServiceTests` 30, `ViewArchiveReaderTests` 17, `ViewArchiveWriterTests` 12, `ViewImportOverlayTests` 4, `ViewArchiveIncludeTests` 3. `AnnotationAuditDiffTests` gains 2. |
| `Backend.CosmosDb/test` | 398 pass against the local Cosmos DB emulator. `CosmosViewImportTests` (new) has 28 test methods; `CosmosViewTransferTests` (renamed) has 22; `JsonPatchTests` gains 2. |
| `Api.Functions/test` | 348 pass. One run had `MachineTokenFallbackTests.MachineKeyFailure_IsServiceUnavailable` fail; it passed in every rerun and covers middleware this phase does not touch. |
| `Cli/test` | 190 pass. |

All 39 projects under `src/Platform/Storyteller` and `src/Platform/Cli` build, and the touched files add no StyleCop warnings. A traversal build of `src/Platform` fails only on `Supervisor/Abstraction/test` (`net6.0`, rejected by `Microsoft.NET.Test.Sdk`), which this phase does not touch.

## Review

Fixed (`bb53635f`):

1. **`Values` numbers lost precision on export and import, so an exported annotation compared unequal and was rewritten.** The cause went beyond the finding. The export's read (`GetAnnotationsAsync`, a LINQ query with the SDK's Newtonsoft serializer) turned numbers into doubles. It also turned date strings into `DateTime`s: `2026-01-01T10:00:00+02:00` was exported shifted to local time, and `….000Z` lost its `.000`. And even with the exact text, a query answers the shortest form of a double while a point read answers 17 digits. The fix has three parts:
   - the stream query of 1.6;
   - `ViewAnnotationDocument.Parse` on `Utf8JsonReader` with numbers as `JRaw` (1.5); `ViewDocumentJson.Parse` lost its now unused `DateParseHandling` parameter;
   - the same-double rule of `AnnotationAuditDiff` (1.5).

   `Transfer_KeepsTheTextOfNumbersAndDatesOfValues` exports and re-imports into the same view with `Overwrite` and with `Skip`, and expects `Unchanged` with only the `Created` audit entry; it failed before the fix. Numbers that do not fit a decimal are not rejected; they travel as their text.
2. **A `$remove` with an invalid JSONPath in a template or configuration file failed the whole job instead of its file.** `JsonExtensions.RemoveRequested` now wraps a Newtonsoft `JsonException`, or an `ArgumentException` from an invalid regular expression in a filter, in `JsonPatchException` (`Invalid`), which the imports report as `Failed` with `PatchInvalid`. A path that selects the whole document (`$`) gets the same treatment instead of an `InvalidOperationException`. A token that one path matches twice is removed once. As a side effect, the configuration and template routes answer `400 PatchInvalid` for these inputs instead of `500`. `Transfer_ReportsABrokenRemoveDirectiveOnItsFileAndImportsTheRest` failed before the fix (the job failed).

Not fixed:

- **`Values` lose precision beyond 17 significant digits**: dropped in verification as not a real defect. After the first fix, numbers travel as the text Cosmos DB answers, and `RoundTrip_KeepsTheTextOfNumbersThatADoubleDoesNotHold` covers it.

## Notes for Phases 2-4

- **Access:** the routes of PR #88 now export and import all four kinds with `Overwrite`, but they check only the `Configuration` scopes. Phase 2 must pass `Include` and `Existing` through, answer `400` for unknown kinds, and check scopes per included kind, using the stored, resolved `Include` when a job is read.
- **Per-file failures:** concurrency exhaustion (`ConflictException` in the annotation import, `SchemaConcurrencyException` in the schema import) still fails the whole job instead of one file.
- **Descendant-type schemas** whose type cannot descend from their key can be written through the API, and they are exported. Their import then fails with `InvalidKey`. Either the export leaves them out or the schema writes reject them; the spec decides neither.
- **Large imports:** each imported schema runs the compliance query of a save, and `Skip` adds one paged configuration listing. The overlay's stored annotation states are read once before the annotation pass, so a concurrent delete is not reflected; the configuration pass of a real run still checks the stored state.
- **Upload check:** `Validate` counts every kind at upload. It could take the request's include if Phase 2 wants a narrower check.
- **Service API:** the parameter lists of `IViewTransferService` are long. Phase 2 may bundle `include`, `existing`, `force` and `dryRun` into an options record.
- **CLI:** `ConfigurationArchives.PackFolder` skips only `.git`, while the server skips every segment that starts with `.` (Phase 3).
- **Admin UI:** `TransferToaster.refresh` calls `invalidateConfiguration` with `item.AnnotationKey!` for every written item. That is `undefined` for templates and type schemas, and annotation items are treated as configurations (Phase 4).
- **Public docs:** `docs/42for.net/platform/configuration.md` and `admin-ui.md` still say an archive holds `<annotation-key>.json` configurations and no templates or schemas (Phase 4).
- **`export-import.md`:** Phases 2-4 rewrite its API, CLI and admin UI sections and drop its "State of the delivery" paragraph.
