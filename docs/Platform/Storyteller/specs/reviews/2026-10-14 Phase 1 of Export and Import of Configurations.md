# Phase 1 of Export and Import of Configurations

## Overview

Phase 1 covers the backend (sections 1.1-1.13) of [Export and Import of Configurations](../2026-10-14%20Export%20and%20Import%20of%20Configurations.md), Linear 424-15.

- **Formats.** A view's own documents can be written as a zip in the folder format (current documents, version files, `export.json`) or as a git repository (one commit per version, a packfile with deltas, an index and the checked-out working tree). An uploaded zip is read back with limits checked before anything is decompressed.
- **Import of one document.** `ImportConfigurationAsync` replaces an annotation's own document as a new version, leaves an equal one alone, and skips or fails with a stable error code.
- **Jobs.** `ConfigurationTransferService` starts, runs and reports exports and imports. Jobs are items in Cosmos DB; archives are blobs and jobs are queue messages in Azure Storage (the new project `Backend.AzureStorage`).

The HTTP endpoints, the queue function, the SDKs, the CLI and the admin UI are Phases 2-4.

## What Was Done

### 1.1 Public model (`Abstractions.Annotations/src/Model/ConfigurationTransfer.cs`)

As specified: `ConfigurationArchiveFormat`, `ConfigurationTransferStatus`, `ConfigurationImportOutcome`, `ConfigurationExportCreate`, `ConfigurationExport`, `ConfigurationImport`, `ConfigurationImportSummary` and `ConfigurationImportItem`. The enums are written as strings.

### 1.2 Error codes

`ErrorCodes` has `TransferNotReady`, `TransferLimit`, `InvalidArchive`, `ArchiveTooLarge`, `AnnotationNotFound`, `EmptyDocument`, `InvalidDocument`, `DuplicateFile` and `SchemaViolation`.

### 1.3 Options (`Transferring/ConfigurationTransferOptions.cs`)

Section `ConfigurationTransfers` with the specified defaults. `AddConfigurationTransfers` (`ConfigurationTransferEntryPoint`) binds and validates them (retention 1-168 hours, a document at most 2 MiB, positive limits) and registers the service and `TimeProvider.System`.

### 1.4 File names (`ConfigurationFileNames`)

- `GetFileName(key, version)` encodes each segment by runes, so a character outside the BMP is encoded as its four UTF-8 bytes when it has to be.
- `TryParse` decodes and checks the result with `AnnotationKey.TryParse`. A decoded segment that contains `.` (`%2E`), an incomplete `%` sequence, an empty segment and a negative or non-numeric version are rejected. The extension is matched without case.
- `GetArchiveFileName` and `ManifestFileName` (`export.json`) live here too.

### 1.5 Documents of a view (`CosmosConfigurationService.GetConfigurationHistoriesAsync`)

- Two queries as specified. The documents come through the stream iterator and the same Newtonsoft settings as every configuration read (`QueryPage<ConfigurationArchiveRow>` in `ListingRows.cs`), so dates parse the same way.
- Keys that do not parse are left out as well. A history item with a version at or above the current one, which should not exist, is dropped.
- `ConfigurationHistory`, `ConfigurationDocument` and `ConfigurationImportResult` are in `Backend.Core/src/Transferring`, one type per file.

### 1.6 Folder format and 1.8 git format (`ConfigurationArchiveWriter`, `Git/*`)

- **Deviation:** the writer is synchronous, `Write(Stream, ConfigurationArchiveSource, format)`, and writes into a `MemoryStream` that the service uploads. Writing a zip straight into a blob stream would block on the network inside `ZipArchive`; the git pack is built in memory anyway, because its name is its checksum.
- `ConfigurationDocumentJson` writes documents with two spaces, `\n` and a final new line, and parses them for the reader (comments allowed, duplicate properties rejected, dates as in Cosmos reads, nothing after the object).
- The git writer is split into `GitObjectId` (SHA-1 of the object header and content), `GitDelta` (copy prefix, insert, copy suffix; copies of at most 64 KiB), `GitPackWriter` (pack and index version 2, `OFS_DELTA`, CRC-32), `GitIndexWriter`, `GitSignature`, `GitPathComparer` (code point order, which is the byte order of UTF-8) and `GitRepositoryWriter`.
- A delta is used only when it is less than half of the object, so small documents are stored whole. The chain is cut at 49 deltas (`MaxDeltaDepth`).
- The pack is stored in the zip without compression; it is zlib data already.
- The zip has directory entries for `.git/refs/tags/`, `.git/objects/info/` and `.git/objects/pack/`, so even an empty repository has the folders git looks for.

### 1.7 Reading an archive (`ConfigurationArchiveReader`)

As specified. Entries are processed in ordinal path order, so the first file of a duplicated key is the first path, whatever the zip order. A file is read only up to one byte over `MaxDocumentBytes`, so a lying declared size cannot exhaust memory.

### 1.9 Import of one document (`CosmosConfigurationService.ImportConfigurationAsync`)

- As specified. The validation runs once, before the write; the write then skips it (`force: true`).
- `CreateOrUpdateConfigurationAsync` and the import share `WriteConfigurationAsync(…, merge)`. With `merge: false`, the document replaces the stored one, and an equal document writes nothing.
- **Fix (not in the spec):** a write onto an item that only cached the effective document (`Content` empty, version 0) restarted at version 1, although a clear had left versions 1…n in the history. The next write then failed with a `409` on the history item. That write now continues after the highest archived version (`GetHighestArchivedVersionAsync`, also used by the create path). `Import_AfterAClearContinuesTheVersions` covers it.

### 1.10 Jobs (`CosmosConfigurationTransferStore`)

- `ConfigurationTransferEntity` keeps a copy of the status (`Status`) for the count of active jobs, the public model of its kind, and `ttl` from `RetentionHours`.
- **Deviation:** `CountActiveAsync` takes `changedSince`. The service counts only jobs changed within the last hour, so a job left `Running` by a crashed worker does not block the project's limit for 24 hours.
- `PartitionKeys.GetTransfer(project)` is `{project}.transfer`.

### 1.11 Archives and the queue (`Backend.AzureStorage`)

- **Deviation:** `IConfigurationArchiveStorage.UploadAsync(name, stream)` replaces `OpenWriteAsync` (see 1.6). Reads use `DownloadStreamingAsync`, so a download can be streamed to the client.
- `AzureStorageConnection` reads `AzureWebJobsStorage` as a connection string, or as an identity-based section with `accountName`, or `blobServiceUri` and `queueServiceUri`, using `DefaultAzureCredential` with `clientId`.
- The container and the queue are created on first use. Queue messages are Base64; `ConfigurationTransferMessage.ToJson` and `TryParse` (in `Backend.Core`) are the format both sides share.
- `AddAzureStorageConfigurationTransfers(configuration, connectionName = "AzureWebJobsStorage")` registers both.

### 1.12 Processing (`ConfigurationTransferService`)

- As specified, with `TimeProvider` for every time.
- A failed job's error is the exception's message for a domain error, otherwise `The export failed: …` or `The import failed: …`; it is cut at 500 characters, like item messages.
- The import saves its progress with the running summary and items, so a client can show counts while it runs.
- The import file name is reduced to its last path segment (backslashes included) and at most 255 characters.

### 1.13 Tests

| Project | Result |
| --- | --- |
| `Backend.Core/test` | 123 pass (89 new in `Transferring/`). `GitTestReader` is an independent reader: it verifies the pack and index checksums, the fan-out, the CRC-32 of every entry, resolves deltas and recomputes every object id, then walks the history. `GitCli_AcceptsTheRepository` extracts an export and runs the installed `git` (2.49): `fsck --full --strict`, `status --porcelain` (empty), `log`, `show HEAD~4:…` and `ls-files`. |
| `Backend.CosmosDb/test` | 274 pass against the Cosmos emulator (11 new in `CosmosConfigurationTransferTests`). |

- **Test infrastructure:** `Startup` built the Testcontainers Cosmos container even when it used the local emulator, and `Build()` requires a running Docker. It is built only when `UseContainer` is set now.

### Documentation

`docs/Platform/Storyteller/configuration-transfer.md` is new: what travels, file names, both formats, the import outcomes, jobs, storage and limits. Phase 2 adds the endpoints.

### Notes for Phase 2

- The queue trigger must use the default connection (`AzureWebJobsStorage`); the clients read the same.
- `Api.Functions` needs `AddConfigurationTransfers` and `AddAzureStorageConfigurationTransfers` in `Program.cs`, plus the queue and timer extensions.
