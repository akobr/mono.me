# Phase 2 of Export and Import of Configurations

## Overview

Phase 2 covers the API, the Functions host, Aspire, the OpenAPI document and both SDKs (sections 2.1-2.7) of [Export and Import of Configurations](../2026-10-14%20Export%20and%20Import%20of%20Configurations.md), Linear 424-15. Five endpoints start and read exports and imports and download an export's archive. A queue-triggered function runs the jobs of Phase 1, and a timer function purges old archives. The OpenAPI document was exported again and the C# and TypeScript SDKs were regenerated. The whole flow was run end to end against the Cosmos emulator and Azurite.

## What Was Done

### 2.1 Routes (`Api.Functions/src/V1/ConfigurationTransferHttp.cs`)

- As specified: `StartConfigurationExport`, `GetConfigurationExport`, `GetConfigurationExportArchive`, `StartConfigurationImport` and `GetConfigurationImport`, tag `Configurations`.
- `Definitions` has the routes (`Exports`, `Export`, `ExportArchive`, `Imports`, `Import`, built on `Configurations`), the route ids, the parameters `dryRun` and `fileName`, `ContentTypes.Zip` and the descriptions.
- Both `POST`s answer `202` with `Location` built from the request URL and the job id.
- Domain errors (`NotFoundException`, `ConflictException` with `TransferLimit` or `TransferNotReady`, `InvalidInputException` with `InvalidArchive`) go through `ExceptionHandlingMiddleware`, like the other endpoints. A missing export or import answers `404` with `NotFound` and a message.
- An unknown numeric `Format` answers `400`; a missing body exports a folder.
- The archive is a `FileStreamResult` over the blob's download stream with `FileDownloadName`, so ASP.NET Core writes `Content-Disposition: attachment; filename=…; filename*=UTF-8''…`.

### 2.2 Access

As specified. Debug builds (`DEV_AUTH`) skip the scope and role checks, as everywhere, so the unit tests cover the endpoint behavior, not the roles; `GetAuditActor` returns `System` there, which is why the local jobs show `CreatedBy: System`.

### 2.3 Background functions (`Api.Functions/src/Transfers/ConfigurationTransferFunctions.cs`)

- `ProcessConfigurationTransfer` (queue `configuration-transfers`, default connection) and `PurgeConfigurationTransfers` (`0 17 * * * *`), as specified.
- **Deviation:** `Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues` is **5.5.4**, not the newest 5.5.5. 5.5.5 brings the WebJobs extension 5.3.8, which needs `Microsoft.Extensions.Hosting` 10.0.3; the Functions host of Core Tools 4.109 (runtime 4.1040) does not ship it and fails at start (`Could not load file or assembly 'Microsoft.Extensions.Options, Version=10.0.0.0'`). 5.5.4 brings 5.3.7, built on `Microsoft.Extensions.Hosting` 8.0.1, which runs on that host and on newer ones.
- **Not in the spec:** `ExceptionHandlingMiddleware` rethrows an unmapped exception of an invocation without an HTTP request. It used to swallow it, which would have completed a queue message whose job never ran. The service catches every failure of a job itself, so this only matters for failures before a job starts (for example Cosmos DB unavailable) and for cancellation.
- `host.json` sets `extensions.queues.batchSize` to 4 and `maxDequeueCount` to 3. `Program.cs` adds `AddConfigurationTransfers` and `AddAzureStorageConfigurationTransfers`.
- **Follow-up (found while closing Phase 4):** with a non-HTTP trigger, the Functions SDK generates `obj/…/WorkerExtensions/WorkerExtensions.csproj`, and the traversal projects `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj` (`**\*.*?proj`) picked it up, so `dotnet build src` failed with `NU1008` under central package management. Both now exclude `bin` and `obj`. The remaining error of `dotnet build src/Platform`, `Supervisor/Abstraction/test` on `net6.0` with `Microsoft.NET.Test.Sdk` 18.3, exists on `main` too.

### 2.4 Upload

As specified: a `Content-Length` over `MaxArchiveBytes` answers `413` before the body is read, the copy stops at the limit, and a body without `PK\x03\x04` or `PK\x05\x06` answers `400 InvalidArchive`.

### 2.5 Aspire

`AppHost.cs` runs Azurite (`AddAzureStorage("storage").RunAsEmulator`) on the ports 10000-10002 with a persistent lifetime. The API gets `AzureWebJobsStorage=UseDevelopmentStorage=true` and waits for the emulator. Built, not run (Docker was not running on the machine).

### 2.6 OpenAPI and SDKs

- The extension documents `byte[]` under `application/zip` as `type: string, format: binary`, for the import body and the archive response, so **no document filter** was needed.
- **Export:** the Debug build was served by the Core Tools `func.exe` 4.109 on port 7072, with Azurite and the Cosmos emulator. `/api/openapi/v3.json` reports `0.9.1.33866`, so the document **replaces** `open.api.v0.9.1.json` in `sdk.typescript` and `Sdk.NSwag` (`servers` set back to `http://localhost:7071/api`). Its diff against the previous `open.api.v0.9.1.json` holds only the version and the new operations and schemas.
- **NSwag:** `ApiSdk.g.cs` regenerated. New on `ConfigurationsApiClient`: `StartConfigurationExportAsync`, `GetConfigurationExportAsync`, `GetConfigurationExportArchiveAsync` (`FileResponse`), `StartConfigurationImportAsync(…, bool? force, bool? dryRun, string fileName, Stream body)` (`StreamContent` with `application/zip`) and `GetConfigurationImportAsync`. The enums are per property, like the existing ones (`ConfigurationExportCreateFormat`, `ConfigurationImportItemOutcome`, …).
- **TypeScript SDK:** `npm run generate` (package version stays `0.9.1`). Orval passes the `Blob` body of `startConfigurationImport` with `Content-Type: application/zip`, so `openapi-transformer.mjs` is unchanged. `readBody` in `lib/fetcher.ts` returns a `Blob` for successful `application/zip` and `application/octet-stream` responses; error bodies are still parsed as JSON. README updated.
- **Merge note:** the unmerged branches `feat/424-10` and `feat/424-14` add `open.api.v0.9.2.json`. Whichever merges last has to export the document again and regenerate both SDKs, or the newest document misses the other's operations.

### 2.7 Tests

| Project | Result |
| --- | --- |
| `Api.Functions/test` | 202 pass. New: `ConfigurationTransferHttpTests` (`202` with `Location`, the default format, an unknown format, `404` through another view, the archive's content type and name, the import options, three kinds of non-zip bodies, `413` by size and by `Content-Length`, the queue function with valid and invalid messages, the timer), `RouteTableTests.ConfigurationTransferRoutes_AreRegistered`, `OpenApiDocumentTests.ConfigurationTransferSchemas_ArePublished`, `ExceptionHandlingMiddlewareTests.Invoke_UnmappedExceptionWithoutHttpRequest_IsRethrown`. `TestHttpRequestData` takes a binary body and a URL. |
| `sdk.typescript` | 62 pass, 1 skipped (as before). New in `fetcher.test.ts`: a zip comes back as a `Blob` with its bytes, a JSON error of an archive request is still a `StorytellerError`, and `startConfigurationImport` sends the `Blob` with its query. `typecheck`, `lint` and `build` pass. |

**End to end** (Debug host on port 7072, Azurite 3.37, Cosmos emulator, an unsigned bearer token that Debug builds decode):

1. Two annotations and three configuration versions written through the API.
2. A git export went `Queued` → `Succeeded` through the queue in about 4 seconds. The downloaded zip passed `git fsck --full --strict`, `git status` was clean, `git log` showed three commits by `account: user_e2e <account.user_e2e@2s.platform>`, and `git show HEAD~2:rst.e2e-payments.json` returned version 1.
3. A folder export had `export.json`, the current files and `rst.e2e-payments.1.json`.
4. The folder, edited (one changed document, one file for a key without an annotation, one broken file) and zipped with Windows' `Compress-Archive`, was imported as a dry run and then for real: `Updated` (version 3 on the real run), `Unchanged` for the subject whose document holds a date, `Skipped` with `AnnotationNotFound`, `Failed` with `InvalidDocument`, and two ignored files.
5. A non-zip body answered `400 InvalidArchive`; an unknown export and an export read through another view answered `404`.

**Observation (not changed):** on this host `POST …/annotations/simple` is routed to `SetAnnotation` with the key `simple` and fails; the literal segment does not win over `{key}` there. The new routes have no such sibling.
