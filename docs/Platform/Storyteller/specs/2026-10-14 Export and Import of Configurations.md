# Export and Import of Configurations

Linear: [424-15 Export and import all configuration as git repository](https://linear.app/424fun/issue/424-15/export-and-import-all-configuration-as-git-repository)

The work is split into four phases. Each phase gets its own review:

1. Backend: archive formats, the export and import jobs, and their storage.
2. API, the Functions host, the OpenAPI document and both SDKs.
3. CLI.
4. Administration UI.

## Problem

1. **Configurations cannot leave the platform.** A view's configurations can only be read one key at a time (`GET …/configuration/{key}`, `sform story config <key>`). There is no backup, no offline copy and no way to review a whole view in an editor or in git.
2. **Configurations cannot be moved in bulk.** Seeding a new project or view, restoring a view after a bad edit, or applying a set of documents prepared outside the platform needs one request per key.
3. **The version history is only visible per key.** Every write keeps the previous version (`cfv` items, 365 days), but nothing shows the history of the whole view in order.

The issue asks for:

| Requirement (424-15) | Section |
| --- | --- |
| Export and import all configuration as a folder in a zip archive | 1.6, 1.7, 2.2 |
| Optionally, a git repository with every version as a commit in the correct order | 1.8 |
| As a simple folder: the current configuration as `<annotation-key>.json`, earlier versions as `<annotation-key>.<version-number>.json` | 1.6 |
| The export runs asynchronously on the server | 1.10, 1.11, 2.3 |
| When it is done, the web UI downloads the zip, named with the date and the time (UTC) | 1.4, 4.2 |
| Raw JSON only: no templates, no secrets, no schemas | 1.5 |
| Import and export in both clients, the CLI and the Administration UI | 3, 4 |

## Current State

### Storage of configurations

- A configuration is the item `{view}.cnf.{key}` in the annotation's partition of the organization container `org.{organization}` (`Backend.CosmosDb/src/Entities/Configurations/ConfigurationEntity.cs`, [inheritance.md](../inheritance.md)).
  - `Content` is the annotation's own document, as written by clients.
  - `CalculatedContent` caches the effective document: ancestors, the type template and the own document merged. `CalculatedContentHash` is its hash.
  - `Version` and `Author` describe the last write of `Content`. `_ts` changes whenever the item changes, including when the effective document is cached.
  - `AnnotationDeletionId` is set while the annotation is soft-deleted; the configuration is hidden.
  - An item with an empty `Content` exists only to cache the effective document of an annotation without its own document (`CosmosConfigurationService.CalculateAndCacheConfigurationAsync`, line 776, `Author = "system"`).
- Each write archives the previous version as `{view}.cfv.{key}.{version}` (`ConfigurationHistoryEntity`: `Content`, `Author`, `CreationTime`, `ttl` of 365 days). Versions only grow; a write after a clear continues after the highest archived version (`CosmosConfigurationService.cs:356-372`).
- `GET …/configuration/{key}` returns the **effective** document (`GetRawConfigurationAsync` returns `CalculatedContent`, `Mapping.cs:15`). Only the versions endpoints return the own document of a version (`ToConfigurationFromContent`, `ConfigurationHistoryEntity.ToConfiguration`).
- Secrets never live in a document. A document holds binding expressions, which only the resolved read evaluates ([binding.md](../binding.md)).

### Writes

- `CreateOrUpdateConfigurationAsync` (`CosmosConfigurationService.cs:329`) **merges** the request into the stored document (`MergeInto`, arrays as union), then applies the `$remove` and `$patch` directives (`JsonExtensions.RemoveRequested`, `ApplyPatchRequested`). There is no way to replace a document as a whole, except a clear followed by a create, which costs two versions.
- Unless `force` is set, a write is validated against the combined schema (`IConfigurationSchemaService.ValidateContentAsync`).
- A write to a soft-deleted annotation throws `ConflictException` with `AnnotationDeleted` (`EnsureAnnotationNotDeletedAsync`, line 982). A write to a key without an annotation is accepted.
- With an `AuditActor`, the write appends `ConfigurationChanged` to the annotation's audit (best effort).

### Hosting and background work

- The Storyteller API is an Azure Functions app in the isolated worker model with HTTP triggers only (`Api.Functions/src/Program.cs`). The middlewares already skip invocations without an HTTP request (`GetHttpRequestDataAsync() is null`).
- Cosmos DB is the only store. There is no queue and no blob storage in Storyteller.
- `AzureWebJobsStorage` is already required by the Functions runtime (`local.settings.json`: `UseDevelopmentStorage=true`). The Aspire host has the Azurite emulator commented out (`Aspire.Host/src/AppHost.cs:3-6`).
- TTL is on for every organization container (`ContainerFactory.cs:193`), so an item's `ttl` is honored.
- `Azure.Storage.Blobs`, `Azure.Storage.Queues`, `Azure.Identity` and `Microsoft.Azure.Functions.Worker.Extensions.Timer` are in `Directory.Packages.props`; the queue trigger extension is not.

### Clients

- `sform story config` (`Cli/src/Commands/Configuration/ConfigGetCommand.cs`) has `set`, `edit`, `patch`, `delete`, `diff` and `versions`, all per key, through the NSwag client (`Sdk.NSwag`).
- The Administration UI lists the configurations of a view (`ui.admin/src/app/pages/[org]/[project]/[view]/configurations.vue`) and edits one document at a time.
- The TypeScript SDK's fetcher returns JSON, or text for every other content type (`sdk.typescript/src/lib/fetcher.ts`, `readBody`). A binary body would be corrupted.

## Design

- **The unit is one view.** Every other configuration screen and command works on a view, and a view is what is compared and promoted. An export of the whole project is a loop over its views (out of scope).
- **An export is a job.** `POST …/configurations/exports` stores a job item in Cosmos, puts a message on a storage queue and answers `202`. A queue-triggered function builds the zip into blob storage. The client polls the job and downloads the archive when it has succeeded. The job and the archive expire after 24 hours.
- **An import is a job too.** `POST …/configurations/imports` takes the zip as the request body, checks it, stores it as a blob and queues the job. The function writes the documents and records the outcome of each file in the job. A dry run reports the outcomes without writing.
- **Only the annotation's own documents travel** (`Content` of the configuration and of its versions). The effective document, templates, schemas, annotations, the audit and resolved bindings stay out.
- **Import writes the current documents, not the history.** Each `<key>.json` replaces the stored document as a new version, with the importing caller as the author, unless it is equal. Version files and git history are ignored on import.
- **The git repository is written by our own code**, as a packfile with deltas, an index and the working tree. No git library and no temporary directory are needed, and the size stays linear in the number of versions.

## Proposed Changes

### Phase 1 — Backend

#### 1.1 Public model (`Abstractions.Annotations/src/Model/ConfigurationTransfer.cs`, new)

```csharp
[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationArchiveFormat
{
    Folder = 0, // current documents and earlier versions as files, with export.json
    Git,        // a git repository: every version is a commit
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationTransferStatus
{
    Queued = 0,
    Running,
    Succeeded,
    Failed,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationImportOutcome
{
    Created = 0, // the key had no document
    Updated,     // the document changed, a new version was written
    Unchanged,   // equal to the stored document, nothing written
    Skipped,     // not imported on purpose: no annotation, deleted annotation, empty document
    Failed,      // invalid file or rejected by the schema
}

public record class ConfigurationExportCreate
{
    public ConfigurationArchiveFormat Format { get; init; }
}

public record class ConfigurationExport
{
    public required string Id { get; init; }
    public required ConfigurationTransferStatus Status { get; init; }
    public required ConfigurationArchiveFormat Format { get; init; }
    public required string FileName { get; init; }        // 1.4
    public required DateTimeOffset CreatedAt { get; init; }
    public required AuditActor CreatedBy { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public int? ConfigurationCount { get; init; }          // keys with a current document
    public int? VersionCount { get; init; }                // every exported version, current ones included
    public long? Size { get; init; }                       // bytes of the zip
    public string? Error { get; init; }                    // set when Failed
}

public record class ConfigurationImport
{
    public required string Id { get; init; }
    public required ConfigurationTransferStatus Status { get; init; }
    public string? FileName { get; init; }                 // the uploaded file's name, for display
    public bool IsDryRun { get; init; }
    public bool IsForced { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required AuditActor CreatedBy { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public int? TotalCount { get; init; }                  // configuration files found
    public int? ProcessedCount { get; init; }              // progress while Running
    public ConfigurationImportSummary? Summary { get; init; }
    public IReadOnlyList<ConfigurationImportItem>? Items { get; init; } // every outcome except Unchanged
    public bool AreItemsTruncated { get; init; }
    public string? Error { get; init; }
}

public record class ConfigurationImportSummary
{
    public int Created { get; init; }
    public int Updated { get; init; }
    public int Unchanged { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int IgnoredFiles { get; init; }                 // version files, export.json, anything else
}

public record class ConfigurationImportItem
{
    public required string Path { get; init; }             // path inside the archive
    public string? AnnotationKey { get; init; }
    public required ConfigurationImportOutcome Outcome { get; init; }
    public ulong? Version { get; init; }                   // the written version (Created, Updated) or the stored one (Unchanged)
    public string? ErrorCode { get; init; }                // Skipped and Failed, see 1.2
    public string? Message { get; init; }                  // at most 500 characters
}
```

A dry run reports `Created` and `Updated` without a `Version`.

#### 1.2 Error codes (`Backend.Core/src/ErrorCodes.cs`)

New stable values:

| Code | Used for |
| --- | --- |
| `TransferNotReady` | `409` on the archive of an export that is not `Succeeded` |
| `TransferLimit` | `409` when the project already has `MaxActiveTransfers` queued or running jobs |
| `InvalidArchive` | `400` for an import body that is not a zip, or breaks a limit (1.3) |
| `ArchiveTooLarge` | `413` for an import body over `MaxArchiveBytes` |
| `AnnotationNotFound` | import item `Skipped`: the view has no annotation with the key |
| `EmptyDocument` | import item `Skipped`: the file is `{}` |
| `InvalidDocument` | import item `Failed`: not JSON, not an object, or too large |
| `DuplicateFile` | import item `Failed`: a second file for the same key |
| `SchemaViolation` | import item `Failed`: rejected by the schema (without `force`) |

`AnnotationDeleted` and `PatchInvalid` (a broken `$patch` directive) are reused for import items.

#### 1.3 Options (`Backend.Core/src/Transferring/ConfigurationTransferOptions.cs`, new)

Section `ConfigurationTransfers`:

| Option | Default | Meaning |
| --- | --- | --- |
| `RetentionHours` | 24 | Jobs and archives are kept this long after their last change (1 to 168) |
| `MaxActiveTransfers` | 3 | Queued or running jobs per project |
| `MaxArchiveBytes` | 20 MiB | Size of an uploaded zip, below the 30 MB request limit of Kestrel |
| `MaxExtractedBytes` | 256 MiB | Sum of the uncompressed sizes of the configuration files in an upload |
| `MaxDocumentBytes` | 2 MiB | Uncompressed size of one file; a Cosmos item cannot be larger |
| `MaxArchiveEntries` | 50,000 | Entries of an uploaded zip |
| `MaxReportedItems` | 1,000 | Items kept in an import job; the summary counts all |

The limits are checked from the zip's central directory before any entry is decompressed, so a zip bomb is rejected at upload.

#### 1.4 File names (`Backend.Core/src/Transferring/ConfigurationFileNames.cs`, new)

- A current document is `<key>.json`, a version `<key>.<version>.json`, as the issue asks. Example: `usg.billing.payments.json`, `usg.billing.payments.3.json`.
- Annotation names may contain characters that file systems reject. Every segment percent-encodes `%`, `<`, `>`, `:`, `"`, `/`, `\`, `|`, `?`, `*` and control characters as `%XX` of their UTF-8 bytes. Names never contain `.`, so the dots stay separators.
- Parsing reverses it. Each type code has a fixed number of segments (`AnnotationKeyExtensions.IsValid`), so one extra numeric segment is unambiguously the version: `rst.payments.3.json` is version 3 of `rst.payments`, never a responsibility named `payments.3`.
- The archive's name: `configurations-{organization}-{project}-{view}-{yyyyMMdd-HHmmss}Z.zip`, with `-git` before the time stamp for the git format. The time is `CreatedAt` of the export, in UTC.

#### 1.5 Documents of a view (`IConfigurationService.GetConfigurationHistoriesAsync`)

```csharp
// The own documents of every configuration in a view and their versions still in the history, for an export.
// Configurations of soft-deleted annotations are left out; an empty document is not a version.
Task<IReadOnlyList<ConfigurationHistory>> GetConfigurationHistoriesAsync(string organization, string project, string view, CancellationToken cancellationToken = default);

public sealed record class ConfigurationHistory
{
    public required string AnnotationKey { get; init; }
    public required IReadOnlyList<ConfigurationDocument> Versions { get; init; } // oldest first
    public ConfigurationDocument? Current { get; init; }  // the last of Versions, or null when the configuration was cleared
}

public sealed record class ConfigurationDocument
{
    public required ulong Version { get; init; }
    public required string Author { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required JObject Content { get; init; }
}
```

`CosmosConfigurationService` runs two cross-partition queries over the project's partitions:

1. `SELECT c.id, c.AnnotationKey, c.Version, c.Content, c.Author, c.CreationTime, c._ts, c.AnnotationDeletionId FROM c WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND (STARTSWITH(c.id, @cnf) OR STARTSWITH(c.id, @cfv))`, with `@cnf = "{view}.cnf."` and `@cfv = "{view}.cfv."`. Items are read with the same Newtonsoft settings as every other configuration read, so dates in documents are parsed the same way.
2. `SELECT VALUE c.AnnotationKey FROM c WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND c.ProjectName = @project AND c.ViewName = @view AND IS_DEFINED(c.AnnotationType) AND IS_DEFINED(c.Deletion)`: the deleted annotations, whose versions `GetConfigurationVersionsAsync` hides too.

Rules:

- A `cnf` item with `AnnotationDeletionId`, and every `cfv` item of a deleted key, is left out.
- A version whose `Content` has no properties is not exported. That drops the cache-only items (`Author = "system"`) and matches `HasContent` of the listing.
- The time of a history version is `CreationTime`; the time of the current version is the item's `_ts`. Both can be later than the write, because caching the effective document also changes `_ts` (Current State). Per key they stay in version order.
- A key with versions but no current document (cleared, or re-created after a delete) has `Current = null`.

#### 1.6 Folder format (`ConfigurationArchiveWriter`, new)

```
export.json
rst.payments.json
rst.payments.1.json
rst.payments.2.json
usg.billing.payments.json
```

- `<key>.json` holds the current document, `<key>.<n>.json` every earlier version still in the history.
- Documents are written as indented JSON (two spaces), `\n` line ends and a final new line, without BOM. The zip entry's time is the version's time.
- `export.json` keeps what file names cannot:

  ```json
  {
    "Format": "Folder",
    "Organization": "acme", "Project": "main", "View": "default",
    "ExportedAt": "2026-10-14T01:30:12Z",
    "ExportedBy": { "Kind": "Account", "Id": "user_01", "Name": "Ada" },
    "Configurations": [
      {
        "AnnotationKey": "rst.payments",
        "File": "rst.payments.json", "Version": 3, "Author": "account: user_01", "UpdatedAt": "…",
        "Versions": [ { "Version": 1, "File": "rst.payments.1.json", "Author": "…", "CreatedAt": "…" } ]
      }
    ]
  }
  ```

  A cleared configuration has `File` and `Version` null.

#### 1.7 Reading an archive (`ConfigurationArchiveReader`, new)

- Every entry is visited, in subfolders too, so a zip of a folder or of a checked-out repository works. Directories, entries under a `.git` folder, `__MACOSX/` and `._*` files are skipped without being counted.
- A `*.json` file whose name parses as a current document (1.4) is a configuration file. Version files, `export.json` and every other file are counted in `IgnoredFiles`.
- Documents are parsed with comments allowed, duplicate properties rejected, and the date handling of the Cosmos reads (`DateParseHandling.DateTime`, `RoundtripKind`), so an exported document compares equal to the stored one.
- A file that is not JSON, not an object, or over `MaxDocumentBytes` becomes a `Failed` item with `InvalidDocument`. A second file for the same key fails with `DuplicateFile`; the first one is imported.
- `Validate(Stream, options)` reads only the central directory: not a zip, more than `MaxArchiveEntries` entries, or more than `MaxExtractedBytes` declared in configuration files throws `InvalidInputException` with `InvalidArchive`. It runs at upload (2.4) and again before processing.

#### 1.8 Git format (`Transferring/Git/*`, new)

The zip holds a git repository with the working tree checked out:

```
.git/HEAD                         ref: refs/heads/main
.git/config                       repositoryformatversion 0, bare false, filemode false
.git/refs/heads/main              the last commit
.git/objects/pack/pack-<sha>.pack every object, with deltas
.git/objects/pack/pack-<sha>.idx  pack index v2
.git/index                        index v2 of the working tree
rst.payments.json                 the working tree: current documents only
```

History:

- Every exported version is one commit that sets `<key>.json` to that version's document. Commits are ordered by the version's time, then the key, then the version number; a merge per key keeps each key's versions in order even when two times are equal.
- Author and committer are the version's author and time. The author string becomes the name, and the e-mail is derived from it: `account: user_01` → `account: user_01 <account.user_01@2s.platform>`, `system` → `system <system@2s.platform>`.
- Messages: `Add rst.payments (version 1)` for the first version of a key in the export, `Update rst.payments to version 3` for the next ones.
- When cleared configurations leave files that the current state does not have, one last commit at the export time removes them: `Remove cleared configurations`, with the keys in the body, by `2S Platform <system@2s.platform>`.
- An empty view gives a repository without commits (an unborn `main`).

Objects (`GitPackWriter`, `GitDelta`, `GitIndexWriter`, `GitObjectId`):

- Object ids are SHA-1 over `"{type} {length}\0{content}"`, data is zlib-compressed (`ZLibStream`).
- The tree is flat. Entries are `100644 {name}\0{20-byte id}`, ordered by the UTF-8 bytes of the name.
- A blob is stored as an `OFS_DELTA` against the previous version of the same file, and a tree against the previous tree, when the base is at most 49 deltas deep; otherwise the object is stored whole. The delta copies the common prefix and suffix of base and target and inserts the middle, which covers the one changed region of every commit. Identical blobs are stored once. Without deltas, every commit would store a full tree, and the archive would grow with versions × configurations.
- The pack is version 2 with a SHA-1 trailer; the index is version 2 with a fan-out table, sorted ids, CRC-32 of each packed entry and 31-bit offsets. The pack is built in memory, because its name is its checksum.
- `.git/index` lists the working tree with the blob ids and sizes, so `git status` of the extracted folder is clean. The stat fields are zero except size and modification time; git compares the content once and refreshes them.

#### 1.9 Import of one document (`IConfigurationService.ImportConfigurationAsync`)

```csharp
// Replaces the annotation's own document with the given one as a new version, unless it is equal.
Task<ConfigurationImportResult> ImportConfigurationAsync(FullKey key, JObject content, string author, bool force = false, bool dryRun = false, AuditActor? actor = null);

public sealed record class ConfigurationImportResult(ConfigurationImportOutcome Outcome, ulong? Version = null, string? ErrorCode = null, string? Message = null);
```

1. A missing annotation is `Skipped` with `AnnotationNotFound`, a deleted one with `AnnotationDeleted` (`AnnotationItemStore.ReadStateAsync`, one point read).
2. `$remove` and `$patch` are applied to the document alone, as on a create. A broken `$patch` is `Failed` with `PatchInvalid`.
3. A document without properties is `Skipped` with `EmptyDocument`. Clearing stays an explicit `DELETE`.
4. Equal to the stored own document (`JToken.DeepEquals`): `Unchanged` with the stored version.
5. Unless `force`, the document is validated against the schema. A violation is `Failed` with `SchemaViolation` and the validator's message.
6. A dry run stops here: `Created` or `Updated` without a version.
7. Otherwise the document is written by the existing write path with **replace** semantics: the previous version goes to the history, the dependent caches are invalidated, the version increments, and with an actor the audit gets `ConfigurationChanged`. `CreateOrUpdateConfigurationAsync` keeps merging; both call one private `WriteConfigurationAsync(…, merge)`.

#### 1.10 Jobs (`IConfigurationTransferStore`, `CosmosConfigurationTransferStore`, new)

- One item per job in the organization container: partition `{project}.transfer`, id `{view}.exp.{id}` or `{view}.imp.{id}`, so a job is only found through the view it was started in. `id` is a new GUID (`N` format).
- The item holds `Kind`, the project and view, the requester's `Author` string (the `Author` of imported versions), and the public model (`Export` or `Import`).
- `ttl` is `RetentionHours`. Every write restarts it, so a job lives that long after its last change. `ExpiresAt` is computed the same way.
- Updates use the item's ETag. A worker that loses the race leaves the job to the other one.
- `CountActiveAsync(organization, project)` counts `Queued` and `Running` jobs of the project in its partition.

#### 1.11 Archives and the queue (`Backend.AzureStorage`, new project)

`42.Platform.Storyteller.Backend.AzureStorage`, with the interfaces in `Backend.Core/src/Transferring`:

```csharp
public interface IConfigurationArchiveStorage
{
    Task<Stream> OpenWriteAsync(string name, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}

public interface IConfigurationTransferQueue
{
    Task EnqueueAsync(ConfigurationTransferMessage message, CancellationToken cancellationToken = default);
}

public sealed record class ConfigurationTransferMessage(string Organization, string Project, string View, ConfigurationTransferKind Kind, string Id);
```

- Blob container and queue are both `configuration-transfers` (`ConfigurationTransferNames`). Blob names: `{organization}/{project}/{view}/exports/{id}.zip` and `…/imports/{id}.zip`. Both are created on first use.
- The connection is `AzureWebJobsStorage`, the account the queue trigger already listens on, so a second connection cannot point the messages at an account nobody reads. A value is a connection string. A section with `blobServiceUri` and `queueServiceUri`, or `accountName`, uses `DefaultAzureCredential` (with `clientId` for a user-assigned identity), like the Functions runtime.
- Queue messages are Base64, the encoding the queue trigger expects.

#### 1.12 Processing (`ConfigurationTransferService`, new, in `Backend.Core`)

`IConfigurationTransferService`:

```csharp
Task<ConfigurationExport> StartExportAsync(string organization, string project, string view, ConfigurationArchiveFormat format, string author, AuditActor actor, CancellationToken cancellationToken = default);
Task<ConfigurationExport?> GetExportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);
Task<(ConfigurationExport Export, Stream Archive)> OpenExportArchiveAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);
Task<ConfigurationImport> StartImportAsync(string organization, string project, string view, Stream archive, string? fileName, bool force, bool dryRun, string author, AuditActor actor, CancellationToken cancellationToken = default);
Task<ConfigurationImport?> GetImportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);
Task ProcessAsync(ConfigurationTransferMessage message, CancellationToken cancellationToken = default);
Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
```

- **Start** checks `MaxActiveTransfers` (`ConflictException`, `TransferLimit`). An import validates the zip (1.7) and uploads it before the job item exists. Then the job is stored as `Queued` and the message is sent. When sending fails, the job is marked `Failed` and the error is rethrown.
- **Process** loads the job. A `Succeeded` or `Failed` job is left alone (a repeated message). Otherwise it becomes `Running` with `StartedAt`, guarded by the ETag.
  - *Export:* `GetConfigurationHistoriesAsync`, then the writer streams the zip into the blob. The job gets `Succeeded`, the counts, and `Size`.
  - *Import:* the blob is read into memory (at most `MaxArchiveBytes`) and read by `ConfigurationArchiveReader`. Files are imported one by one in key order through `ImportConfigurationAsync`. `ProcessedCount` is saved every 50 files or 2 seconds. At the end the job gets `Succeeded`, the summary and up to `MaxReportedItems` items (`AreItemsTruncated`), and the upload is deleted.
  - Any exception marks the job `Failed` with its message (no stack trace) and is logged; a partial export blob is deleted. The message is completed, so a broken archive is not retried. A crash of the worker leaves the job `Running`, and the queue delivers the message again.
- **Purge** deletes archives whose blob is older than `RetentionHours` plus one hour. Job items expire through their `ttl`.
- `TimeProvider` gives every time, so tests control it.
- `services.AddConfigurationTransfers(configuration)` registers the service and binds and validates the options.

#### 1.13 Tests (Phase 1)

- `Backend.Core/test`:
  - `ConfigurationFileNamesTests`: encoding and parsing round trips, versions, segment counts, invalid names.
  - `ConfigurationArchiveWriterTests`: folder layout, `export.json`, document formatting, entry times, cleared keys.
  - `ConfigurationArchiveReaderTests`: subfolders, `.git` and ignored files, duplicates, invalid documents, limits, dates.
  - `GitRepositoryTests`: known object ids (`blob` of `hello\n`), tree order, delta round trip through an in-test delta applier, pack and index structure, index entries. When `git` is on the `PATH`, the extracted repository passes `git fsck --full`, `git log` lists the commits in order with the right authors and dates, `git status --porcelain` is empty, and `git show` of an old commit returns the old document.
  - `ConfigurationTransferServiceTests`, with in-memory fakes of the store, storage, queue and configuration service: start, limit, process of both kinds, dry run, repeated messages, failures, progress, truncation, purge.
- `Backend.CosmosDb/test` (Cosmos emulator):
  - `GetConfigurationHistoriesAsync`: versions, cleared keys, deleted annotations, other views and projects, cache-only items.
  - `ImportConfigurationAsync`: created, updated with replace semantics (a removed property stays removed), unchanged, missing and deleted annotations, empty document, schema violation with and without `force`, dry run, audit entry.
  - `CosmosConfigurationTransferStore`: create, read through the right view only, ETag conflict, active count.

### Phase 2 — API, host and SDKs

#### 2.1 Routes (`Api.Functions/src/V1/ConfigurationTransferHttp.cs`, new)

| Method | Route | Operation id | Answer |
| --- | --- | --- | --- |
| `POST` | `v1/{organization}/{project}/{view}/configurations/exports` | `StartConfigurationExport` | `202` `ConfigurationExport`, `Location` of the job |
| `GET` | `…/configurations/exports/{id}` | `GetConfigurationExport` | `200` `ConfigurationExport`, `404` |
| `GET` | `…/configurations/exports/{id}/archive` | `GetConfigurationExportArchive` | `200` `application/zip` with `Content-Disposition: attachment; filename=…`, `404`, `409` `TransferNotReady` |
| `POST` | `…/configurations/imports?force=&dryRun=&fileName=` | `StartConfigurationImport` | `202` `ConfigurationImport`, `400` `InvalidArchive`, `413` `ArchiveTooLarge` |
| `GET` | `…/configurations/imports/{id}` | `GetConfigurationImport` | `200` `ConfigurationImport`, `404` |

- The tag is `Configurations`, so the NSwag client is `ConfigurationsApiClient`.
- The literal `configurations/…` segments do not collide with `configuration/{key}`; `RouteTableTests` cover the new routes.
- `POST …/exports` takes `ConfigurationExportCreate` as JSON. `POST …/imports` takes the zip as the raw body (`application/zip`). `fileName` is only displayed.
- `409` `TransferLimit` from both `POST`s.

#### 2.2 Access

- Export (all three routes): scopes `Configuration.Read`, `Configuration.Write`, `Default.Read` or `Default.Write`, and any role on the project, like `GET …/configuration/{key}`. The export holds only own documents, which every reader can already read.
- Import (both routes): scopes `Configuration.Write` or `Default.Write`, and `Contributor`, like a configuration write. Reading the import job needs the same, because it reports the documents' keys and errors.
- Machines are allowed with the same scopes.
- The requester is `GetAuthor()` (the `Author` of imported versions) and `GetAuditActor()` (`CreatedBy`, and the audit of each written annotation).

#### 2.3 Background functions (`Api.Functions/src/Transfers/ConfigurationTransferFunctions.cs`, new)

```csharp
[Function("ProcessConfigurationTransfer")]
public Task Process([QueueTrigger(ConfigurationTransferNames.Queue)] string message, CancellationToken cancellationToken);

[Function("PurgeConfigurationTransfers")]
public Task Purge([TimerTrigger("0 17 * * * *")] TimerInfo timer, CancellationToken cancellationToken);
```

- The queue trigger uses the default connection, `AzureWebJobsStorage`. `host.json` sets `maxDequeueCount` to 3 and `batchSize` to 4.
- A message that cannot be deserialized is logged and dropped.
- New packages: `Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues` (in `Directory.Packages.props`), `Microsoft.Azure.Functions.Worker.Extensions.Timer`.
- `Program.cs` adds `AddConfigurationTransfers` and `AddAzureStorageConfigurationTransfers`.

#### 2.4 Upload

`StartConfigurationImport` copies the body into memory with a limit. Over `MaxArchiveBytes` it answers `413` before reading the rest. An empty body or a body without the zip signature (`PK\x03\x04`, or `PK\x05\x06` for an empty zip) answers `400`. The service validates the central directory (1.7).

#### 2.5 Aspire

`Aspire.Host/src/AppHost.cs` runs Azurite on the default ports (`10000`–`10002`) with a persistent lifetime, and the API waits for it, so `UseDevelopmentStorage=true` works unchanged.

#### 2.6 OpenAPI and SDKs

- The zip is documented as `type: string, format: binary` under `application/zip`, in the request of `StartConfigurationImport` and the response of `GetConfigurationExportArchive`. A document filter adds it if the extension does not.
- The document is exported from the Functions host as `open.api.v{version}.json` into `Sdk.NSwag` and `sdk.typescript`. NSwag is regenerated (`FileParameter` body, `FileResponse` archive).
- TypeScript SDK: `readBody` returns a `Blob` for `application/zip` and `application/octet-stream`, so the archive survives. The generated `startConfigurationImport` sends the `Blob` as the body with `Content-Type: application/zip`; if Orval does not, `openapi-transformer.mjs` corrects the document. `npm run generate`, tests, typecheck and build.

#### 2.7 Tests (Phase 2)

- `RouteTableTests`: the five routes.
- `ConfigurationTransferHttpTests`: scopes and roles (a reader cannot import, a machine with read can export), `202` with `Location`, `404` for a job of another view, `409` for an archive that is not ready, `413` and `400` for bodies, `Content-Disposition` of the archive.
- `ConfigurationTransferFunctionsTests`: the queue function passes the message, drops a broken one.
- `OpenApiDocumentTests`: the new schemas, the binary request and response.
- TypeScript SDK: `fetcher.test.ts` for a zip response; a smoke test that the import body is the `Blob`.

### Phase 3 — CLI (`src/Platform/Cli`)

Two subcommands of `sform story config`, on the context's organization, project and view (`-p`, `-v` as everywhere):

```
sform story config export [-f|--format folder|git] [-o|--output <path>] [-x|--extract] [--timeout <minutes>]
sform story config import <path> [--dry-run] [--force] [--timeout <minutes>]
```

- **Export** starts the job, shows the status until it ends (polling every second, then every 2 and 3 seconds), and downloads the archive.
  - Without `--output`, the server's file name in the current directory. With a directory, that file name in it; otherwise the given file.
  - `--extract` unpacks into the output directory instead (default: the file name without `.zip`), which must be empty or missing. Entries are written through `IFileSystem`, and an entry that resolves outside the directory is refused.
  - Prints the counts, the size and the path. A failed job prints its error and returns `ERROR_CRASH`.
- **Import** takes a zip, or a folder that it packs in memory: every `*.json` file below it, without `.git` folders. It shows the status until it ends and prints:
  - the summary (created, updated, unchanged, skipped, failed, ignored files),
  - a table of the items with their outcome, key, version and message, and a note when the list was truncated,
  - for a dry run, `Nothing was written.`

  Exit code `SUCCESS` without failed items, `ERROR_WRONG_INPUT` otherwise (also for `400` and `413`).
- `--timeout` (default 10 minutes) stops waiting and prints the job id; the job continues on the server.
- New: `ConfigExportCommand`, `ConfigImportCommand`, `CommandNames.EXPORT`/`IMPORT`, `Services/ConfigurationArchives.cs` (packing and extraction), `Output/TransferConsoleExtensions.cs`.
- Tests: packing a folder (`MockFileSystem`): `.git` left out, nested files kept, relative paths with `/`; extraction refuses `../` entries; output path resolution.

### Phase 4 — Administration UI (`src/Platform/ui.admin`)

#### 4.1 Entry points

The toolbar of the Configurations page gets **Export** (`i-lucide-download`) for every role and **Import** (`i-lucide-upload`) for `edit` (Contributor). On phones they are icon buttons with labels for screen readers.

#### 4.2 Export (`components/configuration/ConfigurationExportDialog.vue`)

- A dialog with the two formats as radio cards: *Folder* ("current documents and earlier versions as files") and *Git repository* ("every version is a commit"), and **Export**.
- Starting closes the dialog. A toast follows the job (*Preparing the export…*). When it succeeds, the archive is fetched with the bearer token and saved under the job's `FileName` (an object URL and a temporary link), and the toast says how many configurations were exported. A failure shows the job's error.
- The jobs are tracked by a store (`stores/transfers.ts`), not by the page, so leaving the page does not stop the download. Pending export ids are kept in `sessionStorage` and resumed after a reload.
- Polling: every second, then every 2 and 3 seconds; after 15 minutes it stops with an error.

#### 4.3 Import (`components/configuration/ConfigurationImportDialog.vue`)

- A file picker for one `.zip` (`UFileUpload`), and two options: *Only check, write nothing* (dry run) and *Store documents that violate their schema* (force, with the role's hint).
- While the job runs, the dialog shows the progress (`ProcessedCount` of `TotalCount`). Closing the dialog keeps the job running; the toast at the end has **Show result**.
- The result (`ConfigurationImportResult.vue`): badges with the six counts, and a list of the items (outcome badge, key as a link to its configuration, version, message). A dry run has **Import now**, which uploads the same file again without the dry run.
- After a real import with created or updated documents, the catalog is reloaded and the configuration queries are invalidated.
- Errors of the upload (`400`, `409`, `413`) are shown in the dialog with their message.

#### 4.4 Mocks and tests

- `mock/transfer-handlers.ts` (new) with the five routes over the mock database. The export builds a real zip in the folder format from the own documents (`mock/zip.ts`, stored entries with CRC-32). The import reads stored and deflated entries (`DecompressionStream('deflate-raw')`) and writes the documents like the API, with the same outcomes.
- Vitest: the store (start, polling, download, resume, failure), both dialogs, and the zip helpers.
- Playwright: export in both formats ends with a download named `configurations-…Z.zip`; an import of an edited export shows one update and applies it; a reader sees no Import button; axe on both dialogs.

### Documentation

- `docs/Platform/Storyteller/configuration-transfer.md` (new): formats, file names, the git layout, jobs and their lifetime, limits, access, storage and configuration.
- `docs/42for.net/platform/configuration.md`: a short *Export and import* section.
- `docs/42for.net/platform/admin-ui.md`: export and import on the Configurations page.

## Delivery phases

| Phase | Content | Depends on | Review |
| --- | --- | --- | --- |
| **1** | Backend: 1.1-1.13 | — | `docs/Platform/Storyteller/specs/reviews/YYYY-MM-DD Phase 1 of Export and Import of Configurations.md` |
| **2** | API, host, Aspire, OpenAPI document, both SDKs: 2.1-2.7 | 1 | `… Phase 2 of Export and Import of Configurations.md` (same folder) |
| **3** | CLI | 2 | `… Phase 3 of Export and Import of Configurations.md` (same folder) |
| **4** | Administration UI and the public documentation | 2 | `docs/Platform/ui.admin/specs/reviews/YYYY-MM-DD Phase 4 of Export and Import of Configurations.md` |

`configuration-transfer.md` is written in phase 1 and completed in phase 2.

## Files to create / change

| Path | Phase | Change |
| --- | --- | --- |
| `Storyteller/Abstractions.Annotations/src/Model/ConfigurationTransfer.cs` | 1 | new public model |
| `Storyteller/Backend.Core/src/ErrorCodes.cs` | 1 | new codes |
| `Storyteller/Backend.Core/src/Configuring/IConfigurationService.cs` | 1 | `GetConfigurationHistoriesAsync`, `ImportConfigurationAsync` |
| `Storyteller/Backend.Core/src/Transferring/*` | 1 | options, names, records, interfaces, service, archive writer and reader, `Git/*` |
| `Storyteller/Backend.Core/test/*` | 1 | tests |
| `Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs` | 1 | histories, import, replace write |
| `Storyteller/Backend.CosmosDb/src/Entities/Transfers/ConfigurationTransferEntity.cs`, `Transferring/CosmosConfigurationTransferStore.cs`, `EntryPoint.cs`, `PartitionKeys.cs` | 1 | job store |
| `Storyteller/Backend.CosmosDb/test/*` | 1 | tests |
| `Storyteller/Backend.AzureStorage/src/*` | 1 | new project: blob storage, queue, connection, DI |
| `42.mono.slnx`, `Directory.Packages.props` | 1, 2 | new project, queue extension |
| `Storyteller/Api.Functions/src/Definitions.cs`, `V1/ConfigurationTransferHttp.cs`, `Transfers/ConfigurationTransferFunctions.cs`, `Program.cs`, `Api.Functions.csproj`, `host.json` | 2 | API and functions |
| `Storyteller/Api.Functions/src/OpenApi/*` | 2 | binary bodies, if needed |
| `Storyteller/Api.Functions/test/*` | 2 | tests |
| `Aspire.Host/src/AppHost.cs` | 2 | Azurite |
| `Storyteller/Sdk.NSwag/**`, `Storyteller/sdk.typescript/**` | 2 | new document, regenerated SDKs, fetcher |
| `Cli/src/Commands/Configuration/ConfigExportCommand.cs`, `ConfigImportCommand.cs`, `ConfigGetCommand.cs`, `Commands/CommandNames.cs`, `Services/ConfigurationArchives.cs`, `Output/TransferConsoleExtensions.cs` | 3 | commands |
| `Cli/test/*` | 3 | tests |
| `ui.admin/src/app/stores/transfers.ts`, `components/configuration/ConfigurationExportDialog.vue`, `ConfigurationImportDialog.vue`, `ConfigurationImportResult.vue`, `utils/download.ts`, `pages/[org]/[project]/[view]/configurations.vue` | 4 | UI |
| `ui.admin/src/app/mock/transfer-handlers.ts`, `mock/zip.ts`, `mock/all-handlers.ts`, `ui.admin/src/test/**` | 4 | mocks and tests |
| `docs/Platform/Storyteller/configuration-transfer.md`, `docs/42for.net/platform/*.md` | 1-4 | documentation |

## Decisions

1. **One view per export and import.** It matches every other configuration screen and command, and a view is what is compared and promoted.
2. **Jobs, a queue and blob storage**, instead of a long request or work after the response. A request has to end within the HTTP timeout, and a consumption plan may stop the worker after the response. A queue trigger retries after a crash, and a blob holds an archive of any size. `AzureWebJobsStorage` is already required, so no new account is needed.
3. **Import is a job too.** Writing thousands of documents, each with schema validation and cache invalidation, can outlast a request. The upload is checked synchronously, so a broken zip fails at once.
4. **Import replaces documents and writes no history.** The archive's versions cannot keep their numbers, authors or times in the target, and replaying them would create versions that never happened there. Each changed document becomes one new version by the importing caller; equal documents are left alone, so importing an export again changes nothing.
5. **Missing annotations are skipped, not created.** A configuration without an annotation would be invisible in the catalog. Annotations are created by their own endpoints.
6. **Import never deletes.** A configuration missing from the archive keeps its document, and `{}` does not clear one. A mirror mode can follow (out of scope).
7. **Own documents only.** The effective document depends on ancestors and templates that change independently, and the resolved one can contain secrets.
8. **The git repository is written directly.** A git library needs native binaries and a working directory on the Functions host. Loose objects would need a full tree per commit, so the archive would grow with versions × configurations; a pack with prefix and suffix deltas keeps it linear.
9. **One commit per version, in time order.** It is the history the issue asks for. Grouping writes into commits would need a time window that the data does not have.
10. **Times are the stored ones.** The time of a version can be later than its write, by an effective-document cache refresh. Adding a write time to configuration items would fix it for new versions only (out of scope).
11. **Files are flat, named by key.** The issue asks for `<annotation-key>.json`. Reading accepts subfolders, so a reorganized repository still imports.
12. **Jobs and archives expire after 24 hours.** An archive is meant to be downloaded right away; a longer-lived copy belongs to the user.

## Open questions

Decided by the defaults above, to be confirmed:

- export and import per view (decision 1);
- import writes current documents only, never the archive's history (decision 4);
- annotations missing in the target view are skipped (decision 5);
- import does not delete configurations missing from the archive (decision 6);
- archives and jobs expire after 24 hours (decision 12).

## Out of scope

- Exporting or importing annotations, templates and schemas.
- An export of every view of a project in one archive.
- A mirror import that clears configurations missing from the archive.
- Restoring version history from an archive.
- Listing past exports and imports; the notification center of the Administration UI (424-31) can build on the job endpoints.
- Write times on configuration items (decision 10).
