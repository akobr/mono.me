# Phase 1 of Project Settings on the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Overview

Phase 1 delivers the server half of [Project Settings on the Server](../2026-11-15%20Project%20Settings%20on%20the%20Server.md): the catalog of project settings as the conformance kind `project-settings`, the shared model and rules in `Abstractions.Annotations`, `IProjectSettingsService` with its Cosmos DB implementation on the access point, the audit event `ProjectSettingsChange`, `GET`, `PUT` and `PATCH v1/{organization}/{project}/settings`, the settings on `ProjectSummary` and `AccessPoint`, a new OpenAPI snapshot, both SDKs, and the API documentation (spec sections 1 to 6 and the phase-1 rows of section 10).

| Commit | Subject |
| --- | --- |
| `0d00422c7` | feat(platform): add the catalog of project settings to the conformance suite and the shared rules (424-266) |
| `227d291d0` | feat(platform): store project settings on the project and serve them by GET, PUT and PATCH (424-266) |
| `aef7b282b` | feat(platform): add the project settings operations and the catalog types to both SDKs (424-266) |
| `b8ee1dbf7` | docs(platform): describe the project settings of the API (424-266) |
| `f870c1a59` | fix(platform): refuse names of a project settings patch that differ only in case (424-266) |

## What Was Done

### 1. The catalog: conformance kind `project-settings`

As planned, with these details:

- `src/Platform/Storyteller/conformance/project-settings.json`: the catalog (`format`, `typeCodes`, `builtIn`, 18 colors, 65 icons, 5 date formats, defaults, limits, 597 time zones of tz database 2026f) and 72 vectors. A vector has `settings` and either `expected` (the API shape without `Version`/`Modified`, compared regardless of member order) or `problems` (`path` and `rule`, in any order).
- `conformance/tools/time-zones.cs`: a file-based app over NodaTime's `DateTimeZoneProviders.Tzdb.Ids` (canonical ids and links); NodaTime 3.3.7 is in `Directory.Packages.props`. No project globs the file.
- `schema/project-settings.schema.json`; `suite.schema.json` has the kind; `suite.json` lists the file at version `1.1.0`; the README table and run commands are updated.
- Runners: `Backend.Core/test/ProjectSettings/ProjectSettingsConformanceTests.cs` (every vector, plus a re-read of each result in both shapes); `SuiteTests.RunKinds` names it, and a new theory checks that the schema accepts a valid vector and refuses ten invalid ones. The TypeScript SDK runs the kind (`suite.test.ts`, `run`). The conformance workflow runs the .NET class on Linux and Windows and triggers on the new paths (`docs/github/workflows.md` updated).

### 2. The model and the rules

As planned, with these details:

- `Abstractions.Annotations/src/Model/ProjectSettings/`: `ProjectSettings`, `ProjectTypeAppearance`, `ProjectSettingsProblem`, `ProjectSettingsCheck` (`IsValid`), `ProjectSettingsJsonShape` (`Api`, `File`), `ProjectSettingsCatalog` (the embedded resource `_42.Platform.Storyteller.project-settings.json`, `Parse`, `Load`, `TryGet*` lookups that return the canonical spelling, `IndexOfTypeCode`), `ProjectSettingsRules` (`Normalize`, `SameValues`, the rule ids, `All`) and `ProjectSettingsJson` (`Read`, `ToJson`, `ApplyMergePatch`, and `ReadMergePatch` from the review fix).
- Twelve rules: the spec's eleven and `case-collision` (two member names that differ only in case; two type codes that differ only in case stay `type-code`).
- Choices the spec left open, made so that .NET and TypeScript answer the same: `null` means none for every member and for a whole type entry, and `Types: null` means built-in everywhere; case folding is ASCII; trimming uses Unicode White_Space; lengths count UTF-16 code units; a blank color or icon is refused (only labels, plurals, the time zone and the date format treat blank as none); unknown members and codes are reported in the spelling they were given; `SameValues` treats `null` as the default settings.
- `Abstractions.Access` references `Abstractions.Annotations`; `ProjectSummary.Settings` and `AccessPoint.Settings`.
- Tests in `Backend.Core/test/ProjectSettings/` (catalog, rules, JSON reading with the camelCase `exhibit` file, merge patch); they use the root test namespace, because a `…UnitTests.ProjectSettings` namespace hides the type.

### 3. The service and the storage

As planned, with these details:

- `Backend.Core`: `IProjectSettingsService`, `ProjectSettingsWrite`, `ProjectSettingsCondition` (`None`, `Anything`, `Version`, `Of`, `Holds`), `ProjectSettingsInvalidException` (`Problems`), `ProjectSettingsPreconditionFailedException` (`CurrentVersion`), `ErrorCodes.InvalidSettings` and `SettingsPreconditionFailed`. `InvalidInputException` and `PreconditionFailedException` are no longer sealed.
- `Backend.CosmosDb`: `CosmosProjectSettingsService` (read with the ETag, the condition, compute, validate, skip a no-op, replace under the ETag with one retry, then `ConflictException`; the audit event best effort), `ProjectSettingsEntity` and `ProjectTypeAppearanceEntity` on `AccessPointEntity.Settings`, registered by `AddCosmosDbAnnotations`. `GetProjectsAsync` projects `ap.Settings`; `ToAccessPoint` maps it (the defaults for a project, null for an organization).
- `CosmosMachineAuthenticationPolicyStore` replaces under the ETag with one retry instead of upserting (`409` after a second race, documented in OpenAPI and `authentication.md`).
- Tests: `ProjectSettingsServiceTests` (17 on Cosmos DB, every case of the spec), `CosmosMachineAuthenticationPolicyStoreTests` (4), `ProjectSettingsServiceContractTests`, the `GetProjects` expectations, and a check that the base seed's merge keeps the settings.

### 4. Audit

As planned: `KeyValueAuditOperation.ProjectSettingsChange` is the last member; `Detail` is `v{old}→v{new}: Types.sbt, DefaultTimeZone`. It is listed in the `KeyValueUsageHttp` description, the MCP `KeyValueTools`, the sform `--operation` description, `key-values.md`, and, after the SDK regeneration, `coverage-values.json`, `KeyValuesSupportB.AuditOperations` and the `[Covers]` of KV-AUD-05 (which now also expects "No audit events." for this operation; its row in `docs/Platform/Cli.AutomationTests/test-cases.md` follows).

### 5. The HTTP API

As planned, with these details:

- `Api.Functions/src/V1/ProjectSettingsHttp.cs` with `ProjectSettingsConditions`, `RequestBodyLimits.ProjectSettings` (16 KiB), `ProjectSettingsMergePatch`, `ProjectSettingsDocumentFilter`, two response-header types and the `Details` of both new exceptions in `ErrorResponseMapping`. Both writes are in the user-only tables.
- A body that is empty or not JSON answers `400 InvalidSettings` with one problem (`$`, `type`), so `InvalidSettings` always carries `Details`. A PATCH without a `Content-Type` is accepted, as for annotations.
- The merge patch of one type is the named, nullable component `ProjectTypeAppearanceMergePatch` (OpenAPI 3.0 cannot mark a `$ref` nullable, and an inline schema made NSwag generate a class named `Anonymous`).
- Tests: `ProjectSettingsHttpTests` and new rows in `RouteTableTests`, `RouteDispatchTests`, `RequestBodyLimitsTests`, `ErrorResponseMappingTests` and the OpenAPI tables, plus `Document_ProjectSettingsOperations_ArePublished`.

### 6. OpenAPI snapshot and SDKs

As planned, with these details:

- Snapshot `open.api.v0.10.40.json` (`info.version` 0.10.40.8829) in `Sdk.NSwag` (older ones kept) and in `sdk.typescript` (replacing 0.10.34).
- C# SDK regenerated (NSwag 14.6.3); `Sdk.NSwag/src/ApiSdk.ProjectSettings.cs` adds `GetProjectSettingsConditionalAsync` (`ConditionalResponse<ProjectSettings>`, `304` as a result), `SetProjectSettingsVersionedAsync` and `PatchProjectSettingsVersionedAsync` (a `JObject`, sent as `application/merge-patch+json` with its nulls), both returning `VersionedResponse<ProjectSettings>`; tests in `Sdk.NSwag/test/ProjectSettingsTwinsTests.cs`.
- TypeScript SDK regenerated; the PATCH sends `application/merge-patch+json`. `scripts/generate-catalog.mjs` (from `generate.mjs`, formatted with Prettier) writes `generated/catalog/project-settings.ts` (`typeColors`/`TypeColor`, `typeIconNames`/`TypeIconName`, `dateFormats`/`DateFormat`, `catalogTimeZones`/`CatalogTimeZone`, `builtInTypeAppearances`, `projectSettingsDefaults`, `projectSettingsLimits`, `projectSettingsTypeCodes`, `projectSettingsFormat`; no clash with Orval's names). `lib/project-settings.ts`: `projectSettingsEntityTag`, `isTypeColor`, `isTypeIconName`, `isDateFormat`, `isCatalogTimeZone` and `validateProjectSettings` (the .NET rule ids, paths and messages). `lib/json.ts` shares the JSON path helper with `configuration-formats.ts`. Tests: the catalog against the file and the 72 vectors, `project-settings.test.ts`, settings cases in `msw.test.ts`.
- READMEs of both SDKs and `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md`.

### 10. Documentation (phase 1 rows)

As planned, with these details: `access.md` (`## Project settings`: routes, document, defaults, the twelve rules, ETag, conditions, PUT, PATCH, order of checks, limits, storage, audit; `Settings` on `AccessPoint` and `ProjectSummary`; roles and error rows), `authentication.md` (sform machine note, restricted machines, either read scope, user-only routes, Administrator list, `413`, the CORS note, the machine policy `409`), `conformance.md` (the kind, its catalog, the time zone list and its script, vectors, runners, CI), `conformance/README.md`, `key-values.md`, `audit-stamps.md` (`ProjectSettings.Modified`).

### Test results

| Project | Result |
| --- | --- |
| `dotnet build src/Platform`, `src/Libraries/Platform` | 0 errors |
| `Backend.Core/test` | 1573 passed |
| `Api.Functions/test` | 3158 passed |
| `Backend.CosmosDb/test` (container, by class): `*ProjectSettings*`, `*MachineAuthentication*`, `*CosmosAccessServiceTests`, `*KeyValuePolicy*`, `*AccessIsolationTests`, `*KeyValueAuditAndUsageTests` | 17, 4, 58, 15, 7, 8 passed |
| `Sdk.NSwag/test` | 424 passed |
| `Configuration/test`, `ConfigurationFormats/test` | 653 passed (2 skipped, Unix only), 616 passed |
| `Cli/test`, `Mcp/test`, `DbCreator/test` | 2018, 751, 135 passed |
| `Cli.AutomationTests` (no target) | 88 passed (coverage checks included), 258 skipped |
| `Hosting/test`, `Aspire.Hosting/test` | 348, 164 passed |
| TypeScript SDK `npm test`, typecheck, lint, build, `generate` twice without a diff | 483 passed, 1 skipped |

`NameQueryTests.ToCondition_FiltersTheModelsByTheName` failed once in three full `Backend.Core` runs (a regex timeout under parallel load, unrelated); it passed alone and in the later full runs. The Cosmos DB runs were disturbed by another worktree's run on the shared vNext emulator and were repeated on a private one.

### Reviews

Three reviewers (server correctness, spec and tests, SDKs and conformance) reported five findings; one skeptic per finding kept one:

1. **A PATCH resolved member names that differ only in case by their order** (confirmed; fixed in `f870c1a59`). `ProjectSettingsJson.ReadMergePatch` checks the patch's own names at the root, under `Types` and in each type (`case-collision`, `type-code`), merges the rest and reads the result, so a PATCH lists every problem as a PUT does. As a consequence, an unknown member or code in a patch is refused even with a `null` value (before: "remove nothing", `200`). Tests in `ProjectSettingsJsonTests`, `ProjectSettingsHttpTests` and `ProjectSettingsServiceTests`.

Refused: "a PATCH accepts unknown members" (the specified design at the time; covered by the fix), two reports about the CI path filters of `Platform.UI.Admin.yml` (the conformance workflow catches a catalog drift), and "the snapshot is named 0.10.40 while the last API commit is 0.10.41" (the content is identical to an export at HEAD; only `info.version` differs).

### Deviations from the spec

1. The admin UI has 65 icons, not 66; the catalog has 65.
2. A twelfth rule, `case-collision`, next to the spec's eleven.
3. Spec section 11 says DbCreator's base seed (`Data.cs`) drops the settings of `42dotnet`. It merges under the ETag and keeps them; a test checks it.
4. `InvalidInputException` and `PreconditionFailedException` are unsealed for the two new exceptions.
5. The machine authentication policy write can answer `409`.

### Left for later

- Testiny case 460 (KV-AUD-05) still describes nineteen operations; it is updated with the CLI cases in phase 3.
- The admin UI's own tests need `NODE_OPTIONS=--no-experimental-webstorage` on Node 26 (local environment only); the UI work is phase 2.
- The snapshot keeps the name `0.10.40`; if a later phase changes `Api.Functions/src`, the snapshot is exported again.
