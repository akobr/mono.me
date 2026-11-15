# Project Settings on the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Problem

A project has three settings that exist only in the admin UI, in each browser's `localStorage` (`2s-admin:project-settings`, from 424-13 and 424-23): the names, colors and icons of its seven annotation types, the default time zone that new annotations start in, and the date format. Every member configures them again in every browser; one person's change reaches nobody; `sform`, the MCP server and the SDKs never see them; a DbCreator data set cannot carry them (the `exhibit` set ships a `project-settings.json` that has to be applied by hand).

The issue asks for settings of the **project**, stored by the API, validated by shared rules, read by every member and machine of the project, and changed only by a user with the Administrator role; for both SDKs; for `sform` commands that read and write them and use them only for console output that people read; for DbCreator data sets that carry them; and for the MCP server to return them. The admin UI part has its own spec, [Project Settings from the Server](../../ui.admin/specs/2026-11-15%20Project%20Settings%20from%20the%20Server.md).

The inputs are the issue (its sections 1 to 9, its decisions and its agent notes). The issue has no CodeRabbit comment. The branch `feat/424-266` starts from `main` at `7944671e` (data sets of 424-252 merged, `exhibit` added; 424-265 key/value pairs of data sets **not** merged). Line numbers refer to that tree.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The project document and its writers

- A project is the access point `AccessPointEntity` (`Backend.CosmosDb/src/Entities/Access/AccessPointEntity.cs`) in container `core`, partition `access`, id `apt.{org}.{project}`, with `Key`, `AccessMap`, `MachineAuthentication`, `KeyValues` (the key/value policy) and `Description` (`:29`).
- Point reads deserialize with System.Text.Json (`DeserializeSystemTextJson`), writes and queries go through the Cosmos SDK serializer (Newtonsoft, PascalCase, nulls left out, `CosmosClientProvider.cs`). A new nullable property is absent on old documents and read as null by both.
- Writers: membership changes (`CosmosAccessService.UpdateMembershipAsync`, ETag replace, retry once on `412`), the description (`CosmosAccessService.UpdateAccessPointAsync`, `:275-314`, the same), the key/value policy (`CosmosKeyValuePolicyStore.SetAsync`, `IfMatchEtag`), the machine authentication policy (`CosmosMachineAuthenticationPolicyStore.SetAsync`, `:53-74`, reads without an ETag and **upserts**), `CreateAccessPointAsync`, and the DbCreator base seed (`Backend.CosmosDb/src/Data.cs`, upserts fresh documents of `42dotnet` and `42dotnet.main`). All but the last copy the entity with `with { … }`, so a new typed property survives them.
- The closest pattern is the key/value policy on the same document: `KeyValuePolicy` (`Abstractions.Annotations/src/Model/KeyValues/KeyValuePolicy.cs`) with its own `Version` and `Modified`, a version check, `Version + 1`, `Modified = AuditStamp.Of(actor, now)`, and an audit event `PolicyChange` (`Backend.CosmosDb/src/KeyValues/Policy/CosmosKeyValuePolicyService.cs:49-133, 455-482`). A no-op update writes nothing.
- `IAccessService` (`Backend.Core/src/Accessing/IAccessService.cs`) has `GetProjectsAsync` and `UpdateAccessPointAsync` as default members that throw `NotSupportedException` (424-78), so the six hand-written test doubles did not change. `GetProjectsAsync` (`CosmosAccessService.cs:229-273`) projects `Key` and `Description` into `ProjectSummary` (`Abstractions.Access/src/Model/ProjectSummary.cs`).

### Audit

- The only project-level audit trail is the key/value one: `IKeyValueAuditLog` (`Backend.Core/src/KeyValues/Audit/IKeyValueAuditLog.cs`), `CosmosKeyValueAuditLog` (partition `{project}.audit` of `organizations`, ids `kva.{time}.{guid}`, `ttl` 400 days), operations `KeyValueAuditOperation` (`Abstractions.Annotations/src/Model/KeyValues/KeyValueAudit.cs:9-30`, last member `RecipientRemoved`), read by `GET v1/{org}/{project}/key-values/audit` (Administrator), `sform story kv audit`, the MCP tool `get_key_value_audit` and the admin UI's Monitor → Audit tab. `docs/Platform/Storyteller/audit-operations.md` is about the annotation audit only.
- The description change of 424-78 writes no audit event.

### The HTTP API

- Routes are attributes on isolated-worker functions. A project route needs the organization constraint `Definitions.Routes.Constraints.Organization` (`Api.Functions/src/Definitions.cs:27`, `regex(^(?!access$))`), otherwise `v1/access/points/settings` is taken by two routes and `RouteDispatchTests.NoTwoRoutesOfOneMethod_TakeTheSameRequest` fails.
- Read access of project metadata for Reader users and machines with `Annotation.Read` or `Configuration.Read`, restricted machines included: `ViewsHttp.GetViews` (`Api.Functions/src/V1/ViewsHttp.cs:63`, `CheckAccessToProjectMetadataAsync(_access, org, project, AccountRole.Reader, MachineScope.AnnotationRead, MachineScope.ConfigurationRead)`).
- Administrator-only, users-only writes with the machine refused first: `AccessHttp.PutAccessPoint` (`Api.Functions/src/V1/AccessHttp.cs:217-255`, `request.EnsureUser("…")`, then `CheckUserAccessToProjectAsync(…, AccountRole.Administrator)`).
- Conditional requests: weak tags and `304` for configuration reads (`ConditionalReads/ConditionalRequestExtensions.cs`: `OkOrNotModified`, `MatchesIfNoneMatch`, `MarkNoCache`), strong tags and `412` for annotation writes (`ConditionalWrites/WriteConditions.cs`, `AnnotationEntityTags.cs`; malformed headers answer `400 InvalidPrecondition`).
- RFC 7396 exists only inside `Backend.Core/src/Annotating/AnnotationPatch.cs` (private). `AnnotationsHttp` handles `application/merge-patch+json`, `415 InvalidContentType` with `Accept-Patch`, and the nullable patch schema (`OpenApi/Filters/AnnotationDocumentFilter.cs`).
- Bodies: `RequestBodyLimits` (`Api.Functions/src/V1/RequestBodyLimits.cs`, `Control` = 8 KiB), `ReadJsonBodyAsync`, `TryCopyBodyAsync`, `BodyTooLarge` (`413`), tested by `RequestBodyLimitsTests`.
- Errors: `ErrorResponse` (`Message`, `Hint`, `ErrorCode`, `Details`); codes in `Backend.Core/src/ErrorCodes.cs`; `ErrorResponseMapping.TryMap` fills `Details` only for `KeyValueImportInvalidException`.
- OpenAPI tests list every operation: `OperationAccess.UserOnly`, `OpenApiExportTests.UserOnlyOperations`, `MachineOperations()` (each machine operation names its scopes in its `403`), `Document_ProjectOperations_ArePublished` (pins the members of `ProjectSummary`). `RouteTableTests` lists every route.
- The API (and the MCP server) set `InvariantGlobalization=true`. With it, `TimeZoneInfo` cannot map IANA ids on Windows; sform and DbCreator run with ICU. A rule built on `TimeZoneInfo` answers differently in the API, sform and DbCreator.

### The conformance suite and the SDKs

- `conformance/suite.json` lists every file with a `kind` from a closed enum (`schema/suite.schema.json`: `flattening`, `formats`, `errors`, `refresh`). `src/Libraries/Platform/Storyteller/Configuration/test/Conformance/SuiteTests.cs` requires every embedded file to be listed, a schema per kind, every kind run by a reference (`RunKinds`, `:19`), and a `vectors` or `scenarios` array in every file. The TypeScript runner `sdk.typescript/src/test/conformance/suite.test.ts` runs a kind or marks it `notApplicable`. Both .NET test projects embed `conformance/**/*.json`; no production assembly embeds a resource.
- The OpenAPI snapshot is written by `Api.Functions/test/OpenApiExportTests.cs` when `STORYTELLER_OPENAPI_EXPORT` is set; the file name comes from `info.version` (git height). `Sdk.NSwag` keeps every snapshot, NSwag 14.6.3 makes one client per tag (`Sdk.NSwag/README.md`). NSwag returns no response headers and its merge-patch models drop nulls, so ETags and nulls need hand-written twins (`Sdk.NSwag/src/ApiSdk.Annotations.cs`, `ApiSdk.Conditional.cs`, `ConditionalResponse.cs`). The TypeScript SDK regenerates `generated/` with `npm run generate` (CI fails on a diff); a PATCH documented with two media types makes Orval send no `Content-Type`.
- A cross-origin browser cannot read `ETag` (the Function App's CORS cannot expose headers, `docs/Platform/Storyteller/authentication.md` "CORS for browser clients"); it can send `If-Match`.

### The CLI

- No `project` command group exists (`src/Platform/Cli/src/Commands/PlatformCommand.cs:10-15`). `BaseContextCommand` gives `-p|--projectKey`; refusals exit `44`, `412` and `400` exit `2` (`Output/KeyValueConsoleExtensions.cs:274`, `ExitCodeOf`). `story edit` is the If-Match editor flow (`Commands/Storyteller/StorytellerEditCommand.cs:188-345`).
- Types appear in human output in `StorytellerListCommand.cs` (list headers `:207-343`; tree tags `" [responsibility]"`, `" [subject]"`, `" [context]"` at `:453-558` in the lowlight color), `StorytellerDeletedCommand.cs:93` (the Type column, the enum name), `Output/CompositionConsoleExtensions.cs:96-104` (`template {AnnotationType}`). `story get` prints JSON only (no header, no parent links).
- Dates go through `AuditConsoleExtensions.FormatTime/FormatDate` (`:130`, `:135`), `VersionsConsoleExtensions.FormatTime` (`:52`) and `KeyValueTimes.Format` (`Commands/KeyValues/KeyValueFormats.cs:354`): local time, `yyyy-MM-dd HH:mm` or `yyyy-MM-dd`, invariant culture.
- `Output/SformConsole.cs`: `HasConsole` is false without a console (redirected, CI); colors are `ConsoleColor` only; nothing reads `NO_COLOR`; there is no `Output` configuration section and no global flag. The automation runner sets `NO_COLOR=1` and `COLUMNS=1000`. The only disk cache is `auth.discovery.json` (`Authentication/AuthenticationConfigurationResolver.cs:147-226`). User preferences are `Configuration/UserSettingDefinitions.cs` (`sform account settings`).
- `Cli.AutomationTests`: `CommandCoverageTests` (every command, option, argument and fixed value in a `[Covers]`, fixed sets in `coverage-values.json`) and `CatalogueTests` (every row of `docs/Platform/Cli.AutomationTests/test-cases.md` has its test) run on every PR.

### DbCreator

- `DbCreator/src/DataSets/DataSetFolder.cs` (`GetEntries` leaves out `set.json` and `annotations.jsonl` at the root, `ComputeHash` covers every file), `DataSetSeeder.cs` (`Plan` checks input before the database, `SeedSetAsync` steps), `DataSetProvisioner.cs` (creates a missing organization, project and view through `IAccessService.CreateAccessPointAsync`, uses an existing project as it is), outcomes `Created`, `Unchanged`, `Skipped` (`DocumentExists`), the seed record, wrong input exits `2`.
- `data-sets/exhibit/project-settings.json` exists in the admin UI's camelCase shape and is read by nobody (counted as an ignored file). `data-sets/showcase/` is written by the admin UI's `test/unit/mock/showcase-set.test.ts`.

### MCP

- `Mcp/src/Tools/DiscoveryTools.cs`: `list_projects` returns the SDK's `ProjectSummary` list as it is (`GET v1/access/projects`).

## Proposed Changes

### 1. The catalog: conformance kind `project-settings`

1. **File** `conformance/project-settings.json` (`$schema: ./schema/project-settings.schema.json`), the single source of the allowed values:

   ```json
   {
     "$schema": "./schema/project-settings.schema.json",
     "description": "The catalog of project settings and how a settings document is validated and normalized.",
     "catalog": {
       "format": "2s.project-settings.v1",
       "typeCodes": ["rst", "unt", "sbt", "usg", "cnt", "exe", "uxe"],
       "builtIn": {
         "rst": { "label": "Responsibility", "plural": "Responsibilities", "color": "violet", "icon": "i-lucide-…" }
       },
       "colors": ["red", "orange", "amber", "yellow", "lime", "green", "emerald", "teal", "cyan", "sky", "blue", "indigo", "violet", "purple", "fuchsia", "pink", "rose", "gray"],
       "icons": ["i-lucide-target", "…"],
       "dateFormats": ["yyyy-mm-dd", "dd.mm.yyyy", "dd/mm/yyyy", "mm/dd/yyyy", "d mmm yyyy"],
       "defaults": { "timeZone": "UTC", "dateFormat": "yyyy-mm-dd" },
       "limits": { "label": 40, "plural": 40 },
       "timeZones": ["Africa/Abidjan", "…", "UTC", "…"]
     },
     "vectors": [
       { "name": "…", "settings": { … }, "expected": { … } },
       { "name": "…", "settings": { … }, "problems": [ { "path": "$.Types.sbt.Color", "rule": "color" } ] }
     ]
   }
   ```

   - `builtIn`, `colors`, `icons` and the limits are copied from the admin UI (`app/utils/type-appearance.ts`, `app/utils/annotation-key.ts`, `app/utils/dates.ts`), in their order.
   - `timeZones` is the list of IANA time zone ids of the tz database (canonical zones and their links, e.g. `Asia/Calcutta` next to `Asia/Kolkata`, plus `UTC` and `Etc/*`), generated once by a script kept next to the file (`conformance/tools/time-zones.cs`, a file-based `dotnet run` app over NodaTime's TZDB source, which prints the sorted list) and committed. See section 14 for why the list is in the catalog.
   - **Vectors** cover: an empty document; every rule of section 2.2 once accepted and once refused; case-insensitive codes, members, colors, icons, date formats and zones (`europe/prague` → `Europe/Prague`); trimming and blank labels; empty appearances removed; a plural without a label; 40 and 41 characters (counted in UTF-16 code units, as the UI's `String.length`); the zones `Europe/Prague`, `America/Phoenix` and `UTC` accepted, `CET-ish` and `Central Europe Standard Time` refused; an unknown member, an unknown type code, a `Format` that is not the marker; the camelCase `exhibit` shape read as the PascalCase one.
2. **Schema** `conformance/schema/project-settings.schema.json`; `suite.schema.json` gets the kind; `suite.json` lists the file and moves to version `1.1.0`; `conformance/README.md` and `docs/Platform/Storyteller/conformance.md` describe the kind.
3. **Runners.** .NET: `Backend.Core/test/ProjectSettings/ProjectSettingsConformanceTests.cs` runs every vector through `ProjectSettingsJson.Read` (section 2) and compares the normalized result or the problems (`path`, `rule`); `SuiteTests.RunKinds` gets `project-settings` with a comment naming that runner, and the conformance workflow (`.github/workflows/Platform.Storyteller-conformance.yml`) runs that class on Linux and Windows. TypeScript: the SDK runs the vectors through its `validateProjectSettings` (section 6.2) and `suite.test.ts` lists the kind in `run`. The admin UI and sform test their own tables (colors, icons, symbols) against the catalog (their specs and section 7.4).

### 2. The model and the rules (`Abstractions.Annotations`, `Abstractions.Access`)

#### 2.1 Model

In `Abstractions.Annotations/src/Model/ProjectSettings/` (namespace `_42.Platform.Storyteller`, next to `AuditStamp` and `AnnotationTypeCodes`):

```csharp
public sealed record class ProjectSettings
{
    public const string FileFormat = "2s.project-settings.v1";

    // Keyed by lower-case type code (rst, unt, sbt, usg, cnt, exe, uxe); a missing type uses the built-in appearance.
    public IReadOnlyDictionary<string, ProjectTypeAppearance> Types { get; init; } = ImmutableDictionary<string, ProjectTypeAppearance>.Empty;

    public string? DefaultTimeZone { get; init; }   // an id of the catalog's timeZones; null = UTC
    public string? DateFormat { get; init; }        // one of the catalog's dateFormats; null = yyyy-mm-dd
    public long Version { get; init; }              // raised by every change; 0 = never set
    public AuditStamp? Modified { get; init; }      // the last change
}

public sealed record class ProjectTypeAppearance
{
    public string? Label { get; init; }   // the singular name, e.g. "Company"
    public string? Plural { get; init; }  // only with Label; clients derive it when missing
    public string? Color { get; init; }   // a color of the catalog
    public string? Icon { get; init; }    // an icon of the catalog, e.g. "i-lucide-building-2"
}

public sealed record class ProjectSettingsProblem(string Path, string Rule, string Message);
```

Equality of `Types` is by content (the records override `Equals` for it, or `ProjectSettingsRules.SameValues` compares), so "a write whose result equals the stored settings" is decidable.

`Abstractions.Access` gets a project reference to `Abstractions.Annotations` (no cycle exists), and `ProjectSummary` and `AccessPoint` get `ProjectSettings? Settings`.

#### 2.2 Rules (`ProjectSettingsRules`, `ProjectSettingsJson`, `ProjectSettingsCatalog`)

- `ProjectSettingsCatalog` loads the embedded catalog once (`Abstractions.Annotations.csproj`: `<EmbeddedResource Include="..\..\conformance\project-settings.json" LogicalName="_42.Platform.Storyteller.project-settings.json" Link="Catalog\project-settings.json" />`): type codes, built-in appearances, colors, icons, date formats, defaults, limits, time zones (case-insensitive sets returning the canonical spelling).
- `ProjectSettingsJson.Read(JsonNode? node)` → `ProjectSettingsCheck { ProjectSettings? Settings; IReadOnlyList<ProjectSettingsProblem> Problems }` reads a settings document of any client (API body, data set file, sform file). Member names match case-insensitively (as the API reads every body); `Format` is optional and must equal the marker; `Version` and `Modified` are read-only and ignored; any other member is a problem. It then applies the rules.
- `ProjectSettingsRules.Normalize(ProjectSettings input)` → `ProjectSettingsCheck`. Every rule refuses with a problem (path in the canonical spelling, a rule id, a message); nothing is dropped silently:

  | Rule id | Rule |
  | --- | --- |
  | `type-code` | a key of `Types` is one of the seven codes, in any case; stored lower-case; two keys that differ only in case are refused |
  | `label` | `Label` is trimmed; blank means none; at most 40 characters |
  | `plural` | `Plural` as `Label`, and refused without a `Label` (`plural-without-label`) |
  | `color` | one of the catalog's colors, any case, stored as in the catalog |
  | `icon` | one of the catalog's icons, any case; stored as in the catalog |
  | `time-zone` | an id of the catalog's `timeZones`, any case, stored in its canonical spelling; blank means none |
  | `date-format` | one of the catalog's date formats, any case; blank means none |
  | `unknown-member`, `type` | an unknown member, a value of the wrong JSON type |
  | `format` | `Format` present and not `2s.project-settings.v1` |

  An appearance without any value is removed; an empty `Types` means built-in everywhere; a null time zone means UTC and a null date format `yyyy-mm-dd` (null is stored as absent; `UTC` and `yyyy-mm-dd` given explicitly are stored as given).
- `ProjectSettingsJson.ToJson(ProjectSettings, ProjectSettingsJsonShape)` writes the canonical PascalCase shape (`File`: with `Format`, without `Version`/`Modified`, the shape of data sets and `sform project settings -e`; `Api`: without `Format`). Codes are written in the catalog's order.
- `ProjectSettingsJson.ApplyMergePatch(JsonObject target, JsonObject patch)`: RFC 7396 where member names (and type codes) match case-insensitively; `null` removes.
- `ProjectSettingsRules.SameValues(a, b)` compares `Types`, `DefaultTimeZone` and `DateFormat` (not `Version` or `Modified`).

`Abstractions.Annotations` has no test project; the tests go to `Backend.Core/test/ProjectSettings/` (rules, reading, merge patch, catalog loading, the conformance runner).

### 3. The service and the storage (`Backend.Core`, `Backend.CosmosDb`)

1. **`IProjectSettingsService`** (`Backend.Core/src/Accessing/IProjectSettingsService.cs`), a service of its own next to `IAccessService` (as the key/value policy has its own service on the same document):

   ```csharp
   public interface IProjectSettingsService
   {
       // The stored settings, or the defaults (Version 0) when nothing was stored. NotFoundException for an unknown project.
       Task<ProjectSettings> GetAsync(string organization, string project, CancellationToken cancellationToken = default);

       // Replaces Types, DefaultTimeZone and DateFormat (Version and Modified of the input are ignored).
       Task<ProjectSettingsWrite> SetAsync(string organization, string project, ProjectSettings settings, ProjectSettingsCondition condition, KeyValueCaller caller, CancellationToken cancellationToken = default);

       // Applies an RFC 7396 merge patch to the stored settings (in their Api JSON shape) and validates the result like SetAsync.
       Task<ProjectSettingsWrite> PatchAsync(string organization, string project, JsonObject patch, ProjectSettingsCondition condition, KeyValueCaller caller, CancellationToken cancellationToken = default);
   }

   public sealed record class ProjectSettingsWrite(ProjectSettings Settings, bool Changed);

   // IfMatch: null = no condition; "*" matches any; otherwise the versions of a strong If-Match list.
   public sealed record class ProjectSettingsCondition(IReadOnlyList<long>? Versions, bool Any)
   {
       public static ProjectSettingsCondition None { get; } = new(null, false);
       public static ProjectSettingsCondition Version(long version) => new([version], false);
   }
   ```

   `KeyValueCaller` (actor, channel, correlation id) is reused: it is the caller type of the project audit trail.
2. **Errors.** Invalid settings: `ProjectSettingsInvalidException : InvalidInputException` with `ErrorCodes.InvalidSettings` and its `Problems`. A failed condition: `PreconditionFailedException` with the new `ErrorCodes.SettingsPreconditionFailed` and the current version ("The project settings changed meanwhile; they are at version 7."). Two lost races: `ConflictException` (`Conflict`). Unknown project: `NotFoundException`.
3. **`CosmosProjectSettingsService`** (`Backend.CosmosDb/src/Accessing/`), registered by `AddCosmosDbAnnotations` (so the API and DbCreator get it), with `IKeyValueAuditLog?` (optional) and `TimeProvider`:
   1. read `apt.{org}.{project}` with its ETag (point read; `NotFoundException` when missing);
   2. check the condition against the stored `Version` (a missing `Settings` is version 0); a mismatch throws the `412` exception and is never retried;
   3. compute the new settings (`SetAsync`: the input; `PatchAsync`: `ApplyMergePatch` over `ToJson(stored, Api)`, then `Read`), validate (`ProjectSettingsInvalidException` with every problem);
   4. when `SameValues(stored, new)`: return `Changed = false`, write nothing, no event;
   5. replace `entity with { Settings = ProjectSettingsEntity.From(new with { Version = stored.Version + 1, Modified = AuditStamp.Of(caller.Actor, now) }) }` with `IfMatchEtag`; on a Cosmos `412` go back to step 1 once; a second `412` throws `ConflictException`;
   6. append the audit event (section 4).

   It never upserts and never rebuilds the entity from the model, so `AccessMap`, `MachineAuthentication`, `KeyValues` and `Description` survive. Once written, `Settings` is never set back to null (a reset stores empty settings with a higher `Version`), so an ETag never repeats.
4. **`ProjectSettingsEntity`** (`Backend.CosmosDb/src/Entities/Access/`): `Types` (`Dictionary<string, ProjectTypeAppearanceEntity>?`, no default entries), `DefaultTimeZone`, `DateFormat` (strings), `Version` (`long`), `Modified` (as `KeyValuePolicy.Modified` is stored). No `required` members (System.Text.Json point reads), no enums. `AccessPointEntity.Settings` is nullable, absent on old documents; no migration.
5. **`IAccessService.GetProjectsAsync`** projects `c.Settings` too, so `ProjectSummary.Settings` is filled (the defaults when the document has none); `AccessMappings.ToAccessPoint` maps `Settings` for `GET v1/access/points/{key}`.
6. **The machine authentication policy store** gets the same ETag replace with one retry instead of its upsert, so a concurrent policy save cannot drop a settings change (small, contained; its tests cover it).

Tests:

- `Backend.Core/test/ProjectSettings/`: the rules, reading (case-insensitive members, unknown members, the camelCase file), merge patch, `SameValues`, the catalog, the conformance runner.
- `Backend.CosmosDb/test/ProjectSettingsServiceTests.cs` (Cosmos, run with `STORYTELLER_TESTS_USE_CONTAINER=true` and `--filter-class`): defaults of a new project; set and get; patch and remove with `null`; a no-op keeps the version and writes no event; `If-Match` match, mismatch (`412` with the version), `*`; invalid input refused with every problem; unknown project `404`; the other members of the access point survive a settings write and a settings change survives a membership change, a description change, a key/value policy change and a machine policy change; the audit event; `GetProjectsAsync` returns the settings.

### 4. Audit (`ProjectSettingsChange`)

- `KeyValueAuditOperation` gets a new last member `ProjectSettingsChange` (after `RecipientRemoved`, so existing numbers stay), in the project's audit trail (`{project}.audit`), which the Monitor → Audit tab, `sform story kv audit` and `get_key_value_audit` read.
- The event has the caller's actor and channel (`User`, or `System` for DbCreator), no view or key, and `Detail` = `"v{old}→v{new}: {changed parts}"`, where the parts are `Types.{code}` for each changed type, `DefaultTimeZone` and `DateFormat` (names only, never values).
- It is written after the replace; a failed append is logged as a warning and does not fail the write (the change is stored; a retry would be a no-op).
- Every list of the operations learns it: `Api.Functions/src/V1/KeyValueUsageHttp.cs:42`, `Mcp/src/Tools/KeyValueTools.cs`, `src/Platform/Cli/src/Commands/KeyValues/KeyValueAuditCommand.cs:23`, `Cli.AutomationTests/src/coverage-values.json` and its `[Covers]`, the admin UI's `AuditTrail.vue` filter and mock (UI spec), `docs/Platform/Storyteller/key-values.md` (Audit trail), both SDKs.

### 5. The HTTP API (`Api.Functions`)

| Method | Route | Operation | Access |
| --- | --- | --- | --- |
| GET | `v1/{organization}/{project}/settings` | `GetProjectSettings` | Users: `Reader`. Machines: `Annotation.Read` or `Configuration.Read`, also a machine restricted to one annotation |
| PUT | `v1/{organization}/{project}/settings` | `SetProjectSettings` | Users with `Administrator` (or Owner). Machines: `403 AccessDenied` "Changing the project settings needs a user with the Administrator role.", before any other check |
| PATCH | `v1/{organization}/{project}/settings` | `PatchProjectSettings` | As PUT |

The three functions live in `Api.Functions/src/V1/ProjectSettingsHttp.cs`, on the route template with the organization constraint. `settings` is a reserved project and view name already (`NameRules.ReservedProjectAndViewNames`).

- **ETag.** The strong tag `"<Version>"` (e.g. `"0"`, `"7"`), documented as the quoted `Version` of the body, so a browser that cannot read the header builds `If-Match` from the body.
- **GET** answers `200 ProjectSettings` (when nothing was set: `Types` `{}`, no `DefaultTimeZone`, no `DateFormat`, `Version` `0`; never `404` for an existing project), with `ETag` and `Cache-Control: private, no-cache` (`MarkNoCache`), and `304` for a matching `If-None-Match` (weak comparison, `OkOrNotModified`).
- **PUT** reads the body (at most `RequestBodyLimits.ProjectSettings` = 16 KiB, else `413`) as a `JsonNode` through `ProjectSettingsJson.Read` and replaces the settings: types left out go back to built-in, a missing time zone or date format goes back to the default. `Version`, `Modified` and `Format` in the body are accepted and ignored, so a GET result or a data set file can be sent back as it is.
- **PATCH** takes `application/merge-patch+json` (documented) or `application/json`; another content type answers `415 InvalidContentType` with `Accept-Patch: application/merge-patch+json`. The body must be a JSON object (`400 PatchInvalid` otherwise); the merged result is validated as for PUT.
- **Conditions** (PUT and PATCH): `If-Match` is optional; `*` or a list of strong tags; a weak tag never matches; a malformed header answers `400 InvalidPrecondition`; a mismatch answers `412 SettingsPreconditionFailed` with `Details: { "Version": <current> }`. `If-None-Match` on a write answers `400 InvalidPrecondition` ("The project settings always exist; a write takes If-Match.").
- **Answers** of PUT and PATCH: `200` with the stored settings and the `ETag` (also for a write that changed nothing); `400 InvalidSettings` with `Details` = the list of problems (`Path`, `Rule`, `Message`) and a `Message` that names the first problem and their count; `404` for an unknown project; `409 Conflict` after the retries.
- **`GET v1/access/projects`** answers `ProjectSummary` with `Settings` (every project of the caller, the defaults when nothing was set), so the admin UI's overview gets every project's settings in one request. **`GET v1/access/points/{key}`** includes `Settings` of a project for its Administrators.
- **The time zone of new annotations** stays a client default: the API creates an annotation without a `TimeZone` exactly as today.
- `ErrorResponseMapping` gets the `Details` of the two new exceptions; `ErrorCodes` gets `InvalidSettings` and `SettingsPreconditionFailed`.
- **OpenAPI**: tag `Access` (so the C# SDK keeps the operations on `IAccessApiClient` and no new client is registered anywhere); schemas `ProjectSettings`, `ProjectTypeAppearance` and `ProjectSettingsMergePatch` (every member optional and nullable through a document filter like `AnnotationDocumentFilter`); `ETag` as a response header type; `If-Match` / `If-None-Match` parameters; the `304`, `400`, `403`, `404`, `409`, `412`, `413` and `415` answers. Colors, icons and date formats are strings whose descriptions point to the catalog (no OpenAPI enums, so the SDKs carry no second copy of the lists).
- **Tests** (`Api.Functions/test`): `ProjectSettingsHttpTests` (a fake `IProjectSettingsService` and access checks: Reader and machine reads, restricted machine read, Contributor and machine writes refused, Administrator writes, the machine refused before the scope check, conditions and their `400`/`412`, `304`, `415`, `413`, `400` with details, a no-op write); `RouteTableTests` and `RouteDispatchTests` rows; `RequestBodyLimitsTests` (the largest valid body, every character escaped, fits 16 KiB); `ErrorResponseMappingTests` rows; the OpenAPI tables (`UserOnly`, `MachineOperations()` with the 403 text naming both read scopes, `Document_ProjectOperations_ArePublished` with `Settings`).
- **Docs**: `docs/Platform/Storyteller/access.md` (a `## Project settings` section after Projects, the routes, the roles table, the error codes), `authentication.md` (user-only routes, restricted machines, body limits).

### 6. OpenAPI snapshot and SDKs

#### 6.1 C# SDK (`Sdk.NSwag`)

- A new snapshot `open.api.v<major>.<minor>.<patch>.json`, exported after the last commit that touches `Api.Functions/src`, regenerated with NSwag (`Sdk.NSwag/README.md`).
- Hand-written twins in `Sdk.NSwag/src/ApiSdk.ProjectSettings.cs` for what NSwag cannot do: `GetProjectSettingsConditionalAsync(org, project, string? ifNoneMatch)` (the value, the `ETag`, `304`), `SetProjectSettingsVersionedAsync(org, project, ProjectSettings body, string? ifMatch)` and `PatchProjectSettingsVersionedAsync(org, project, JToken patch, string? ifMatch)` (sends `application/merge-patch+json` with explicit nulls), each returning the settings and the `ETag`, and mapping errors as the generated client does.

#### 6.2 TypeScript SDK (`sdk.typescript`)

- `npm run generate` with the new snapshot (fetch client, Vue Query composables `useGetProjectSettings`, `useSetProjectSettings`, `usePatchProjectSettings`, Zod schemas, MSW mocks). The PATCH is documented with `application/merge-patch+json` only, so Orval sends that content type.
- `scripts/generate-catalog.mjs`, run by `generate.mjs` after Orval, writes `generated/catalog/project-settings.ts` from `conformance/project-settings.json` (`as const` arrays and the types `TypeColor`, `TypeIconName`, `DateFormat`, plus the built-in appearances, defaults, limits and time zones), formatted like Orval's output, so CI's "generated matches" check guards it too. If a name clashes with an Orval model, the catalog names get a `ProjectSettings` prefix.
- `lib/project-settings.ts`: `projectSettingsEntityTag(version)` (`"<Version>"`), guards (`isTypeColor`, `isTypeIconName`, `isDateFormat`, `isCatalogTimeZone`), and `validateProjectSettings(document)` with the rules of section 2.2 (returns the normalized settings or the problems). `test/conformance/project-settings.test.ts` checks the exported catalog against the file and runs every vector; `suite.test.ts` lists the kind in `run`.

### 7. CLI (`sform`)

#### 7.1 Commands

A new top-level group `sform project` (help only, `CommandNames.PROJECT`) with `settings`; every command is a `BaseContextCommand` (`-p <organization>.<project>`; `-v` is ignored as by other project-level commands):

```text
sform project settings [-f|--format text|json] [-e|--export <file>]
sform project settings set [-i|--import <file>] [--replace]
                           [--type <code> [--label <text>] [--plural <text>] [--color <name>] [--icon <name>] [--clear]]
                           [--time-zone <iana-id>] [--date-format <format>]
                           [--expected-version <n>] [--dry-run]
sform project settings edit
sform project settings reset [--types] [--time-zone] [--date-format] [-y|--yes]
```

- **`settings`** prints a table of the seven types (code, built-in name, the project's name and plural, the color as a swatch in that color, the icon name and, where icons are shown, its symbol), then the default time zone, the date format (with "(default)" when unset), the version and who changed them last (`Modified`). `--format json` prints the settings in the API shape; `-e` writes the file shape (`Format` marker, section 2.2) that data sets use.
- **`set`** builds a merge patch: the `--import` file (any settings document, read by `ProjectSettingsJson.Read`) first, then the options. `--type` takes a type code or a built-in name (`sbt`, `subject`, any case), never a project label; `--label`, `--plural`, `--color` and `--icon` (`building-2` or `i-lucide-building-2`) need `--type`, and `--clear` removes the type's entry. One `--type` per run (several types: `--import` or `edit`). `--replace` sends the resulting document as a `PUT` (types left out go back to built-in). `--expected-version` sends `If-Match: "<n>"`. `--dry-run` reads the current settings, applies the change locally, validates it with `ProjectSettingsRules` and prints the diff, without a write. The command prints the diff (`WriteDiff`) and "Saved the project settings, version 8." (or "The project settings did not change; they stay at version 7."). Without any change option it refuses with exit `2`.
- **`edit`** reads the settings with their `ETag`, writes the file shape to a temporary file, opens the configured editor (`EditorService`), validates the result locally (problems are printed and the edit is kept in a file, exit `2`), shows the diff, asks for a confirmation and sends a `PUT` with `If-Match`; a `412` keeps the edit in a file and prints how to apply it, as `story edit` does.
- **`reset`** sends a merge patch that removes `Types`, `DefaultTimeZone` and `DateFormat`, or only the parts named, after a confirmation; `-y` skips it, and without a console and without `-y` it exits `43`.
- **Errors**: `403` prints the API's message and exits `44`; `412` prints "The project settings changed meanwhile; they are at version N." and exits `2`; `400` prints each problem as `<path>: <message>` and exits `2`.
- A successful `set`, `edit` or `reset` refreshes the cache of section 7.3.

#### 7.2 Settings in other commands: output only, never input

Outside `project settings`, the settings change only how `sform` shows things to a person. They never change what `sform` accepts (type codes and keys in arguments and options, `--tree-root subject|sbt|responsibility|rst`, `-x` paths, input files, ISO dates in options), what it writes (`story set` writes no `TimeZone`), help and error texts, machine-readable output (`--format json|flat|properties|env|yaml`, `--json`, `--content-only`, `-e` files, `story export`, `config export --github-env`, `kv export`), or anything printed when `SformConsole.HasConsole` is false (redirected output, CI): then the built-in names and today's dates are printed, without colors and icons, byte for byte as today.

Where they apply (with a console):

| Place | Built-in today | With project settings |
| --- | --- | --- |
| `story list --tree`, `--tree-root rst|sbt` | `name [subject]` in the lowlight color | `name [Company]` in the type's color, the icon before the tag (`🏢 [Company]`) |
| `story list` headers | `# Subjects`, `# Responsibilities`, … | `# Companies`, `# Solutions`, … |
| `story deleted` Type column | `Responsibility`, `UnitOfExecution`, … | the project's label, colored, with the icon |
| `story config composition` sources | `template Unit` | `template Job` |
| `story import`/`export` result tables (`TransferConsoleExtensions.FormatDocument`) | the type name | the label |
| dates in tables: `history` (`When`), `deleted` (`Deleted`, `Purged on`), `versions` of configurations, templates and schemas, `kv list`/`versions`/`recipients`/`audit`, `kv usage` header, the deleted notice of `story get` and `story delete` | `yyyy-MM-dd HH:mm`, `yyyy-MM-dd` (local time) | the date in the project's format (`dd.MM.yyyy`, `dd/MM/yyyy`, `MM/dd/yyyy`, `d MMM yyyy` with English months), the time unchanged (local time) |

Sentences (`No subject exists yet.`, continuation hints), the high-precision instants of `history` changes, `story get` (JSON only, no header exists), shared certificate dates (not project data) and ISO timestamps of `account` and `machine` stay as they are. The project's default time zone is shown only by `project settings`.

#### 7.3 Loading (`IProjectSettingsProvider`)

- `Services/ProjectSettings/ProjectSettingsProvider.cs` gives `Task<ProjectDisplay> GetDisplayAsync(CancellationToken)`. A command that renders a type or a date asks for it once; the provider then reads `GET …/settings` for the context project (the C# twin with `If-None-Match`).
- The result is cached in `CliPaths.StateDirectory` (so per `SFORM_HOME`) in `project-settings.cache.json`, keyed by base URL and `org.project`, with its `ETag` and the time of the read; within 10 minutes it is used without a request, after that revalidated (`304` keeps it). `project settings set`, `edit` and `reset` write the new settings into it. The file is written best effort, through `IFileSystem`.
- Any failure (offline, `401`, `403` such as a machine without a read scope, `404` of an older API, a broken cache file) falls back to the built-in display silently, with one debug log line; a command never fails because of settings.
- `ProjectDisplay.BuiltIn` is used, without a request, when there is no console, when `Output:ProjectSettings` is `builtin`, when `SFORM_PROJECT_SETTINGS=builtin`, or with `--builtin`.

#### 7.4 Colors, icons and switches

- **Colors.** `TypeColors` maps the 18 names to the admin UI's 500 shades (24-bit) and to the nearest `ConsoleColor`. 24-bit ANSI is written where sform writes the escape codes itself (the tree renderer) and the terminal supports it (`COLORTERM=truecolor|24bit`, `WT_SESSION`); Alba tables carry `ConsoleColor` only and use the nearest one. `NO_COLOR` (any non-empty value) and no console turn type colors off (the tag falls back to the lowlight color).
- **Icons.** `TypeIconSymbols` maps every icon of the catalog to one emoji of exactly two UTF-16 code units with emoji presentation (a surrogate pair, or a BMP symbol with `U+FE0F`), so the string length used for column widths equals the two cells it takes. A unit test checks that every catalog icon has a symbol. `Output:Icons` = `auto` (default) | `always` | `never`, overridden by `SFORM_ICONS=auto|always|never`. `auto` shows icons when there is a console, the output encoding is UTF-8 and the terminal is known to draw emoji two cells wide: `WT_SESSION` (Windows Terminal), `TERM_PROGRAM` = `vscode`, `iTerm.app`, `WezTerm`, `ghostty`, `Apple_Terminal`, `Hyper`, `TERM` = `xterm-kitty`, `xterm-ghostty`, `wezterm`, `alacritty`, `foot`, `VTE_VERSION` (GNOME and other VTE terminals) or `KONSOLE_VERSION`; elsewhere (the classic Windows console host, unknown terminals) they are left out.
- **Switches.** A new configuration section `output` (`ConfigurationSections.OUTPUT`, `OutputOptions { Icons, ProjectSettings }`) with the user preferences `output.icons` (`auto|always|never`) and `output.projectSettings` (`project|builtin`) in `UserSettingDefinitions` (so `sform account settings set output.icons never` works), the environment variables `SFORM_ICONS` and `SFORM_PROJECT_SETTINGS`, and an inherited root flag `--builtin` for one run.

#### 7.5 Tests

- Unit (`src/Platform/Cli/test`): the commands (get text and JSON, export, set as patch and replace, import, `--type` by code and built-in name, `--clear`, `--dry-run`, edit, reset with and without parts, `403`, `412`, `400`, no change), the provider (cache hit, `304`, expiry, fallback on errors, no console, switches), rendering (with and without a console, `NO_COLOR`, true color, icon detection per terminal variable, `always`/`never`, widths with icons, dates in each format, built-in output byte-identical), input unaffected (`--tree-root solution` refused, `story set` writes no time zone), `TypeIconSymbols` and `TypeColors` against the catalog, `CommandConstructionTests`.
- Automation (`Cli.AutomationTests`): a class `ProjectSettingsTests` under "Account and Access Points" (references `ACC-PRS-nn`, new cases in Testiny `2SC`, rows and per-case bullets in `docs/Platform/Cli.AutomationTests/test-cases.md`, counts updated there, in its README and in `CLAUDE.md`): a Reader reads the settings of the target project; an Owner sets, replaces, imports, exports, edits (fake editor) and resets in a disposable project (`[DisposableProject]`); a stale `--expected-version`; an invalid value with `--dry-run` and without; a Reader's and a Contributor's write refused with `44`; a machine's write refused; the tree shows the project's names in a pseudo terminal and the built-in ones when redirected and with `--builtin`. The shared target project never gets settings. `coverage-values.json` gets the fixed values of `--format`, `--color`, `--icon`, `--date-format`, the new user preferences and `ProjectSettingsChange`.
- Docs: `docs/42for.net/cli/sform.md` (a "Project settings" section: the commands; where names, colors, icons and dates appear; how to turn them off).

### 8. DbCreator data sets

- **The file.** A set may hold `project-settings.json` at its root, in the file shape of section 2.2 (`Format` `2s.project-settings.v1`, PascalCase; read case-insensitively). `DataSetFolder.GetEntries` leaves it out of the documents; `ComputeHash` covers it already.
- **Validation** in `DataSetSeeder.Plan`, before anything is written: `DataSetProjectSettings.Read` (`ProjectSettingsJson.Read`); an invalid file is wrong input (exit `2`) and the message names the file and each path. Two named sets of one run with settings that differ (`SameValues`) are refused; equal settings are written once.
- **Seeding**, after the target is provisioned and before the annotations, as an item of its own kind ("Project settings"):
  - **Created**: the project's settings are at version 0; the seed writes them through `IProjectSettingsService.SetAsync` with `If-Match` version 0 and `KeyValueCaller.System()`;
  - **Unchanged**: the stored settings are equal;
  - **Skipped** (`DocumentExists`): the stored settings differ; a seed never overwrites or merges;
  - **dry run**: decides and writes nothing.

  The outcome is part of the seed output, the seed result and the seed record; `2sInitDb sets` shows "project settings: N types, time zone, date format" for a set that has them.
- **Built-in sets.** `exhibit`: `project-settings.json` rewritten into the file shape (`America/Phoenix`, `yyyy-mm-dd`), its README updated. `showcase`: the admin UI's `showcase-set.test.ts` writes `data-sets/showcase/project-settings.json` from the mock's `fabrikam/invoicing-suite` settings (UI spec).
- **Tests.** `DbCreator/test`: reading, refusals (an unknown color, a Windows time zone, an unknown date format, an unknown member), two sets that disagree, the hash, `GetEntries`, `BuiltInSetsTests` (`exhibit` and `showcase` no longer count `project-settings.json` as ignored; `exhibit` still ignores its README and `key-values.json` until 424-265). `DbCreator/testint`: created, unchanged on a second run (no write, the access point's ETag unchanged), skipped when different, dry run.
- **Docs**: `docs/Platform/Storyteller/data-sets.md` (a `project-settings.json` section, the seed steps and outputs, the `exhibit` set in the built-in table).

### 9. MCP server

- `list_projects` returns `ProjectSummary` with `Settings` once the C# SDK is regenerated; its description says so (type names, default time zone, date format of each project).
- `McpInstructions.Text` gets one line: talk in the project's type names (for example "Solution CRM"), but use the type codes and keys in calls.
- A host test (`McpTestHost`, `RecordingApiHandler`) checks that the settings come through. The MCP server does not write settings.
- `docs/Platform/Storyteller/mcp.md`: the Discovery table, and "project settings writes" under Limits.

### 10. Documentation

| File | Change |
| --- | --- |
| `docs/Platform/Storyteller/access.md` | `## Project settings`: routes, roles, ETag, conditions, merge patch, errors (phase 1) |
| `docs/Platform/Storyteller/authentication.md` | user-only routes, restricted machines, body limits, CORS note on `If-Match` built from `Version` (phase 1) |
| `docs/Platform/Storyteller/conformance.md`, `conformance/README.md` | the kind `project-settings`, the catalog, the time zone list and its script, the runners (phase 1) |
| `docs/Platform/Storyteller/key-values.md` | `ProjectSettingsChange` in the audit trail (phase 1) |
| `Sdk.NSwag/README.md`, `sdk.typescript/README.md` | the snapshot, the twins, the catalog module (phase 1) |
| `docs/42for.net/cli/sform.md` | "Project settings" (phase 3) |
| `docs/Platform/Cli.AutomationTests/test-cases.md`, its README, `CLAUDE.md` counts | the new cases (phase 3) |
| `docs/Platform/Storyteller/data-sets.md` | `project-settings.json`, `exhibit` (phase 4) |
| `docs/Platform/Storyteller/mcp.md` | discovery (phase 5) |
| `docs/42for.net/platform/admin-ui.md`, `ui.admin/README.md` | UI spec (phase 2) |

### 11. Concurrency and known risks

- Writes are ETag-guarded with one retry; the settings `Version` is the client-facing condition. With section 3.6 every runtime writer of the access point replaces under its ETag.
- The DbCreator base seed (`Data.cs`) still upserts fresh `42dotnet` and `42dotnet.main` documents and drops their settings, as it drops their description and policies today (known since 424-78; out of scope).
- A browser on another origin gets no `ETag`; it builds `If-Match` from `Version`, and the preflight must allow `If-Match` (as for annotations).

### 12. Phases and commits

Every commit subject ends with `(424-266)`.

1. **Server and SDKs** (this spec, sections 1 to 6): `feat(platform): add the catalog of project settings to the conformance suite and the shared rules`; `feat(platform): store project settings on the project and serve them by GET, PUT and PATCH`; `feat(platform): add the project settings operations and the catalog types to both SDKs`; `docs(platform): describe the project settings of the API`; the review `Phase 1 of Project Settings on the Server`.
2. **Admin UI** (UI spec), with its review in `docs/Platform/ui.admin/specs/reviews/`.
3. **CLI** (section 7): `feat(platform): read and change the project settings with sform project settings`; `feat(platform): show the project's type names, colors, icons and date format in the console output of sform`; `test(platform): automate the sform project settings cases`; `docs(platform): describe the project settings in sform`; the review `Phase 3 …`.
4. **DbCreator** (section 8): `feat(platform): seed project settings with data sets`; the review `Phase 4 …`.
5. **MCP and documentation** (sections 9, 10): `feat(platform): return the project settings from the MCP discovery`; remaining docs; the review `Phase 5 …`.

### 13. Verification

```bash
dotnet build src/Platform
dotnet test --project src/Platform/Storyteller/Backend.Core/test
dotnet test --project src/Platform/Storyteller/Api.Functions/test
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*ProjectSettings*"
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*CosmosAccessServiceTests"
dotnet test --project src/Libraries/Platform/Storyteller/Configuration/test
dotnet test --project src/Libraries/Platform/Storyteller/ConfigurationFormats/test
(cd src/Platform/Storyteller/sdk.typescript/src && npm ci && npm run generate && git diff --exit-code -- generated && npm test && npm run build)
dotnet test --project src/Platform/Cli/test
dotnet test --project src/Platform/Cli.AutomationTests/src --treenode-filter "/**[Category=Coverage]"
dotnet test --project src/Platform/Storyteller/DbCreator/test
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/DbCreator/testint
dotnet test --project src/Platform/Storyteller/Mcp/test
```

### 14. Differences from the issue

1. **The time zone list is in the catalog.** The issue checks zones with each runtime (`TimeZoneInfo` with ICU on the server, `Intl` in the browser). The API and the MCP server run with `InvariantGlobalization=true`, under which `TimeZoneInfo` cannot resolve IANA ids on Windows (local development) and resolves them from the image's tzdata on Linux, while sform and DbCreator use ICU: the same id would be accepted by one and refused by another. The catalog's list (the tz database's ids, links included, so every browser's spelling is in it) gives one answer everywhere; the conformance vectors still pin `Europe/Prague`, `America/Phoenix`, `UTC`, `CET-ish` and `Central Europe Standard Time`. The UI additionally needs `Intl` to know a zone to render it.
2. **`IProjectSettingsService` instead of two `IAccessService` methods**, as the key/value policy has its own service on the same document: it takes the audit log, a `TimeProvider` and cancellation tokens without changing `CosmosAccessService`'s constructor or its test doubles. `IAccessService` changes only in its projection of `ProjectSummary`.
3. **Names**: `ProjectTypeAppearance` instead of `TypeAppearance` (the admin UI and the TypeScript SDK already have a `TypeAppearance`, and sform imports both the abstractions and the SDK namespaces); error codes `InvalidSettings` (as in the issue) and `SettingsPreconditionFailed`; a dedicated 16 KiB body limit instead of `Control` (8 KiB is too tight for the largest valid body with every character escaped).
4. **`sform project settings set` takes one `--type` per run.** McMaster keeps the values of each option but not their order across options, so `--type a --label x --type b --label y` cannot be paired reliably; several types go through `--import` or `edit`.
5. **`story get` has no header or parent links** to rename (it prints JSON); sentences keep the built-in names, and only headers, tags, type columns and composition sources follow the project.
6. **24-bit colors in trees only**; tables use the nearest `ConsoleColor` (Alba grids carry `ConsoleColor`).
7. **User preferences `output.icons` and `output.projectSettings`** in `sform account settings`, next to `Output:*` and the environment variables, so a person sets the switches once.
8. **The audit event lives in the key/value audit trail** (`KeyValueAuditOperation.ProjectSettingsChange`): it is the project's only audit trail; `audit-operations.md` covers annotations only.

### Out of scope

- Renaming type codes or keys; personal Preferences on the server; carrying settings in view exports and imports; the time zone of new annotations on the server; sform personal date formats; the `Data.cs` base seed dropping settings of `42dotnet`.

### Merge notes

- 424-265 (`feat/424-265`, key/value pairs of data sets) edits the same DbCreator files (`DataSetFolder.GetEntries`, `DataSetContent`, `Plan`, the seed steps, the seed item kinds, record and result, `DataSetOutput`, `BuiltInSetsTests`). Whichever merges second keeps both root files out of the documents and both seed steps (settings before key/value pairs and annotations).
