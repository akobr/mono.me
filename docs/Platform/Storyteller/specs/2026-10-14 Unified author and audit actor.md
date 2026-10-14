# Unified author and audit actor

Linear: [424-30 United author property and AuditActor](https://linear.app/424fun/issue/424-30/united-author-property-and-auditactor)

## Problem

Storyteller records who changed something in two unrelated ways:

- **Annotations** use typed records. `Created` and `Modified` are `AuditStamp { At, By }`, `By` is an `AuditActor { Kind, Id, Name }`, and the audit entries and deletions carry an `AuditActor` as well.
- **Everything else** uses a plain string. Configurations, configuration templates, configuration schemas, their versions and listing summaries carry `Author`. Key/value pairs carry `Author`, `CreatedBy` and `DeletedBy`, the project policy `Author`, the client-encryption recipients `CreatedBy`, the key/value audit events `Actor`, and views `CreatedBy`. The string is built by `GetAuthor()` as `account: {sub}`, `application: {machineId}` or `system`. The time sits next to it in a field with yet another name: `UpdatedAt`, `CreatedAt`, `CreationTime`, `DeletedAt`, or nothing at all (summaries derive it from `_ts`).

The consequences:

1. **Two shapes for one concept.** An API client needs two code paths to show "changed by". The string mixes a kind and an id in an ad-hoc format (`application` for a machine, with a space after the colon) that clients have to parse to link to a member or a machine.
2. **No display name for most records.** The admin UI and `sform` print `account: user_01J…` for every configuration, template, schema and key/value version, while the annotation history next to it shows "Ada Lovelace".
3. **Imprecise version times.** A configuration version's time is the configuration item's `_ts`. The item is also rewritten by the system when its calculated (effective) document is cached or invalidated, so the listed "updated" time and the time of an archived version move without anyone changing the configuration.
4. **Two parameters for one caller.** The configuration writes take both `string author` and `AuditActor? actor`, from `GetAuthor()` and `GetAuditActor()` of the same request.

The issue asks for one type for the actor of a change that references a user reasonably, is simple to use through the APIs, and is cheap in Cosmos DB request units.

## Current State

### Models (`Abstractions.Annotations/src/Model/`)

| Type | Actor fields | Time fields |
| --- | --- | --- |
| `AuditActor.cs:16` `AuditActor` | `Kind` (`Account`, `Machine`, `System`), `Id`, `Name?` | – |
| `AuditActor.cs:27` `AuditStamp` | `By` | `At` |
| `Annotation.cs`, `IAnnotation.cs` | `Created`, `Modified` (`AuditStamp?`) | in the stamps |
| `AnnotationDeletion.cs:16` | `DeletedBy: AuditActor` | `DeletedAt` |
| `AnnotationAudit.cs:119` `AnnotationAuditEntry` | `Actor: AuditActor` | `At` |
| `Configuration.cs:14` | `Author: string` | – |
| `ConfigurationTemplate.cs:13`, `ConfigurationSchema.cs:17` | `Author: string` | – |
| `ConfigurationVersion.cs:9` | `Author: string` | `CreationTime`, `ExpirationTime` |
| `ConfigurationSummary.cs:17,58,71` (configuration, schema and template summaries) | `Author: string?` | `UpdatedAt` (`_ts`) |
| `View.cs:14` | `CreatedBy: string?` | `CreatedAt` |
| `KeyValues/KeyValue.cs:27-39` | `Author`, `CreatedBy`, `DeletedBy` | `UpdatedAt`, `CreatedAt`, `DeletedAt` |
| `KeyValues/KeyValueSummary.cs:21` | `Author` | `UpdatedAt` |
| `KeyValues/KeyValueVersion.cs:12` | `Author` | `CreationTime`, `ExpirationTime` |
| `KeyValues/KeyValuePolicy.cs:32` `KeyValuePolicy` | `Author?` | `UpdatedAt?` |
| `KeyValues/KeyValuePolicy.cs:117` `ClientEncryptionRecipient` | `CreatedBy` | `CreatedAt` |
| `KeyValues/KeyValueAudit.cs:49` `KeyValueAuditEvent` | `Actor: string` | `Time` |

### Where the actor comes from

- `Api.Functions/src/Security/HttpRequestDataExtensions.cs:107` `GetAuthor()` returns `application: {machineId}` or `account: {sub}`.
- `HttpRequestDataExtensions.cs:120` `GetAuditActor()` returns `Machine` with the machine id, or `Account` with `sub` and the `name` claim (falling back to the unique name). It never calls the profile resolver. Under `DEV_AUTH` (Debug builds) it always returns `System`, even when the request carries a decoded token.
- `HttpRequestDataExtensions.cs:152` `GetSecretRightsAsync` builds the `KeyValueCaller` from `GetAuthor()`.
- `Backend.Core/src/KeyValues/KeyValueCaller.cs:4` `KeyValueCaller(string Author, …)`, and `KeyValueCaller.System(string author = "system")`.
- Service signatures with `string author`: `IConfigurationService.CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync` (`Backend.Core/src/Configuring/IConfigurationService.cs:30,32`, next to `AuditActor? actor`), `IConfigurationTemplateService` (`:12,14`), `IConfigurationSchemaService` (`:12,18,24`), `IViewService` (`Backend.Core/src/Annotating/IViewService.cs:10,13`).
- `Api.Web/src/Controllers/AnnotationsController.cs:656` builds an `AuditActor` from the `ClaimsPrincipal` for its annotation writes.

### Storage (`Backend.CosmosDb`)

- Organization containers index every path (`/*`) except `/Entries/*`, the audit entries of annotations (`ContainerFactory.cs:18,215-226`). Every property of an `AuditStamp` therefore produces index terms: `Created.At`, `Created.By.Kind`, `Created.By.Id`, `Created.By.Name`, and the same for `Modified`.
- The Cosmos client ignores null values on write (`CosmosClientProvider.cs`, `IgnoreNullValues = true`), and Newtonsoft deserialization ignores unknown members (`EntryPoint.cs`, `MissingMemberHandling.Ignore`). Views and the key/value policy are read with System.Text.Json, which also ignores unknown members but enforces `required` members.
- Entities with a string author: `ConfigurationEntity`, `ConfigurationHistoryEntity` (with `CreationTime`), `ConfigurationSchemaEntity`, `ConfigurationSchemaHistoryEntity`, `GenerateTemplateEntity`, `GenerateTemplateHistoryEntity`, `ViewEntity` (`CreatedBy`, `CreatedAt`), `KeyValueEntity` (`Author`, `UpdatedAt`, `CreatedBy`, `CreatedAt`, `DeletedBy`, `DeletedAt`), `KeyValueVersionEntity` (`Author`, `CreationTime`), `KeyValueAuditEntity` (`Actor`). The policy and its recipients are stored as `KeyValuePolicy` on the `AccessPointEntity`.
- The history item of a configuration, schema or template takes its `CreationTime` from the current item's `_ts` (`CosmosConfigurationService.cs:424,499,579,973`, `SchemaMappings.cs:33,56`, `TemplateMappings.cs:29,52`). Listings project `c.Author` and `c._ts` (`ListingRows.cs`, `CosmosConfigurationService.cs:77`).
- System writes use `"system"`: the cached calculation item (`CosmosConfigurationService.cs:796`), the unstored version 0 (`Mapping.cs:54`) and the key/value maintenance (`CosmosKeyValueMaintenanceService.cs:285,313`).
- Key/value listings filter the deleted state with `IS_DEFINED(c.DeletedAt)` (`CosmosKeyValueService.cs:662,666,969,973`), and the version lifetime is computed from `UpdatedAt` (`:800`).

### Clients

- The OpenAPI document (`open.api.v0.9.6.json`) and the generated SDKs (`Sdk.NSwag/src/ApiSdk.g.cs`, `sdk.typescript/src/generated`) carry the string fields.
- `sform` prints them raw (`Cli/src/Output/VersionsConsoleExtensions.cs`, `KeyValueConsoleExtensions.cs`), while `AuditConsoleExtensions.FormatActor` already formats an `AuditActor` for the annotation history.
- The admin UI prints them raw (`VersionTimeline.vue:30`, `configurations.vue:98`, `schemas.vue:111`, `templates.vue:60`, `key-values.vue:179`, `KeyValueDetail.vue:254,258`, `KeyValueVersions.vue:101`, `views.vue:108`), while `components/annotations/AuditActorLink.vue` renders an `AuditActor` with a name and a link for the annotation history.

## Analysis

### What identifies an actor

| Candidate | Stable | Unique | Personal data | Verdict |
| --- | --- | --- | --- | --- |
| Identity provider subject (`sub`) | yes, for the life of the account | yes, per provider | pseudonymous | **the reference**. It is `Account.Id`, the key of the account and of every access map |
| E-mail / unique name | changes | mostly | yes | display only |
| Display name | changes | no | yes | display only |
| Machine id | yes | yes | no | **the reference** for machines; it is what authentication puts into the request |

An account and a machine share no id space (`user_01…` or an Entra object id against a machine id), but a client still needs to know which list to look an id up in. A separate `Kind` says that without parsing, so the reference is the pair `Kind` + `Id`. `System` covers writes the platform does itself (calculation caches, maintenance, key rotation).

### Options

**A. One string reference everywhere**, for example `account:user_01…`. It is the smallest (one property, one index term), and equality filters are trivial. But it has no display name, so every list of versions or keys needs the names resolved: a members read per account or machine, which costs request units on every listing and fails for accounts that have left the project. Clients also have to parse the prefix again. This is the problem we have today, only with a cleaner format.

**B. `AuditActor` with a resolved name on read.** The name is always current, but every read of a version list or a listing must look up accounts. That costs request units and latency on the hottest paths, to show a label.

**C. `AuditActor { Kind, Id, Name? }` stored with a display snapshot**, as the annotations already do. One record for every actor. `Kind` + `Id` is the reference, and `Name` is the name the token carried when the change was made. Nothing is looked up on read, the label survives the account's removal from the project, and clients that need the current name or a link resolve `Id` themselves (the admin UI already does this for annotations). The cost is a few bytes per stamp and index terms, which the indexing policy removes (section 5).

**Decision: C.** It is the only option that is cheap on read, renders without extra calls, and is already the established contract of the annotation API. The name may go stale after a rename. That is acceptable for an audit label, and `Id` stays authoritative.

### One stamp, consistent names

Most records keep "who" and "when" together, and today the "when" has five names. The stamp already exists, so every who-and-when pair becomes an `AuditStamp`, with one rule for the property names:

| Property | Meaning |
| --- | --- |
| `Created` | The write that created the object. It never changes. |
| `Modified` | The write that produced the current state. On a version, the write that produced that version, so a document's `Modified` equals the `Modified` of its newest version. |
| `Deleted` | The soft delete, present only while the object is deleted. |
| `Actor` | On audit-log entries, which have their own time field. |

`AnnotationDeletion` keeps `DeletedAt` and `DeletedBy: AuditActor`. It is a record of a deletion operation (id, root, descendant count, purge time) rather than a stamp on the item; its actor already is the unified type, and `DeletedAt` is the sort key of the deleted listing (`ORDER BY c.Deletion.DeletedAt DESC`).

### Request units

Cosmos DB charges a write by the size of the item and by the number of indexed properties ([Request Units](https://learn.microsoft.com/azure/cosmos-db/request-units): "the number of RUs consumed to write a document increases as the property count increases … you can limit the number of RUs consumed by write operations by limiting the number of indexed properties"). A point read is charged by size only.

- **Size.** A stamp is about 110-150 bytes (`{"At":"2026-10-14T08:00:47.576+00:00","By":{"Kind":"Account","Id":"user_01J…","Name":"Ada Lovelace"}}`). The string author it replaces is 30-40 bytes, and on configurations, templates and schemas the stamp also adds the version time that was missing. The difference stays within the same kilobyte for nearly every item, so reads do not change.
- **Indexing.** A stamp has four leaf paths. `Name` is never filtered or sorted on, and it is the only free-text, high-cardinality field among them, so it is excluded from the index. `At` (sorting by change time), `By.Id` (a future "changed by me") and `By.Kind` (a short enum) stay indexed. The same exclusion also lowers the cost of every annotation write, whose two stamps index the name today.
- **Queries.** Listings project `c.Modified` instead of `c.Author` and `c._ts`. Filters on the deleted state of key/value pairs use `IS_DEFINED(c.Deleted)`, as they used `c.DeletedAt`.
- **No lookups.** Option C adds no read to any request.

The review records request charges measured on the emulator before and after.

### Stored data

There is no migration. Following the earlier specs, items stay readable and converge on their next write:

- **Configurations, configuration history, schemas, templates and their history, views.** These have been stored for months. Their entities keep the old fields as nullable, read-only legacy properties, and the mapping builds the stamp from them: `account: {sub}` → `Account`, `application: {id}` → `Machine`, `system` or nothing → `System`, anything else → `Account` with the text as `Id`. The time comes from the old time field, or from `_ts` where there was none. A write stores only the stamp; the legacy properties are null and so are not written.
- **Key/value pairs, versions, audit events, the policy and its recipients.** The key/value store was merged into `main` on 2026-10-14, the day of this plan, and no deployment holds key/value data to keep. They switch to the new shape without a compatibility path (decision D3). A development database drops its `kv.`, `kvv.` and `kva.` items and the `KeyValues` property of its access points, or is recreated.

## Proposed Changes

### 1. Model (`Abstractions.Annotations`)

`AuditActor.cs` keeps both types and gains factory methods. The comment states the rule.

```csharp
[JsonConverter(typeof(StringEnumConverter))]
public enum AuditActorKind
{
    Account = 0,
    Machine,
    System,
}

// Who did something. Kind and Id are the reference: the account's subject (sub, the account id), the machine id,
// or "system". Name is a display snapshot taken when the change was made; it is never looked up and may be stale.
public record class AuditActor
{
    public required AuditActorKind Kind { get; init; }

    public required string Id { get; init; }

    public string? Name { get; init; }

    public static AuditActor System { get; } = new() { Kind = AuditActorKind.System, Id = "system" };

    public static AuditActor Account(string id, string? name = null) => new() { Kind = AuditActorKind.Account, Id = id, Name = name };

    public static AuditActor Machine(string id, string? name = null) => new() { Kind = AuditActorKind.Machine, Id = id, Name = name };
}

// Who made a change and when. Created: the write that created the object; Modified: the write that produced the
// current state (or the listed version); Deleted: the soft delete, only while deleted.
public record class AuditStamp
{
    public required DateTimeOffset At { get; init; }

    public required AuditActor By { get; init; }

    public static AuditStamp Of(AuditActor by, DateTimeOffset at) => new() { At = at, By = by };
}
```

Field changes (removed → added):

| Type | Removed | Added |
| --- | --- | --- |
| `Configuration` | `Author` | `required AuditStamp Modified` |
| `ConfigurationTemplate` | `Author` | `required AuditStamp Modified` |
| `ConfigurationSchema` | `Author` | `required AuditStamp Modified` |
| `ConfigurationVersion` | `Author`, `CreationTime` | `required AuditStamp Modified` (`Version`, `ExpirationTime` stay) |
| `ConfigurationSummary`, `ConfigurationSchemaSummary`, `ConfigurationTemplateSummary` | `Author`, `UpdatedAt` | `AuditStamp? Modified` |
| `View` | `CreatedAt`, `CreatedBy` | `AuditStamp? Created` (null for a discovered or implicit view) |
| `KeyValue` | `Author`, `UpdatedAt`, `CreatedBy`, `CreatedAt`, `DeletedAt`, `DeletedBy` | `required AuditStamp Created`, `required AuditStamp Modified`, `AuditStamp? Deleted` (`IsDeleted`, `ExpiresAt` stay) |
| `KeyValueSummary` | `Author`, `UpdatedAt` | `required AuditStamp Modified` |
| `KeyValueVersion` | `Author`, `CreationTime` | `required AuditStamp Modified` |
| `KeyValuePolicy` | `Author`, `UpdatedAt` | `AuditStamp? Modified` (null for the defaults) |
| `ClientEncryptionRecipient` | `CreatedAt`, `CreatedBy` | `required AuditStamp Created` |
| `KeyValueAuditEvent` | `Actor: string` | `required AuditActor Actor` |

`Annotation`, `IAnnotation`, `AnnotationDeletion` and `AnnotationAuditEntry` do not change.

A configuration that has never been written (version 0, calculated on read and not stored) gets `Modified = { At = the calculation time, By = System }`. The cached calculation item created by a read stores the same.

### 2. Callers and services (`Backend.Core`)

Every `string author` becomes `AuditActor actor`:

```csharp
// IConfigurationService
Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force = false);
Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, AuditActor actor, bool force = false);
// ClearConfigurationAsync and DeleteAsync keep their optional actor: they only write the annotation audit entry.

// IConfigurationTemplateService
Task<ConfigurationTemplate> CreateOrUpdateTemplateAsync(string organization, string project, string view, string annotationType, JObject value, AuditActor actor);
Task<ConfigurationTemplate> PatchTemplateAsync(string organization, string project, string view, string annotationType, JArray patchOperations, AuditActor actor);

// IConfigurationSchemaService: SetSchemaAsync, SetAnnotationSchemaAsync, SetDescendantTypeSchemaAsync take AuditActor actor.
// IViewService: CreateViewAsync and UpdateViewAsync take AuditActor actor.

// KeyValueCaller
public sealed record KeyValueCaller(AuditActor Actor, bool CanReadSecrets, bool CanWriteSecrets, KeyValueChannel Channel = KeyValueChannel.User)
{
    public static KeyValueCaller WithSecrets(AuditActor actor);
    public static KeyValueCaller WithoutSecrets(AuditActor actor);
    public static KeyValueCaller System() => new(AuditActor.System, true, true, KeyValueChannel.System);
}
```

The configuration writes now always audit the annotation (`ConfigurationChanged`), because the actor is no longer optional. The API passed one on every call already. The binding-read aggregation of the key/value audit (`CosmosKeyValueAuditLog.BindingReadKey`) keys on the `AuditActor`.

### 3. Storage (`Backend.CosmosDb`)

- **Entities.** Each entity stores the stamps of its model: `ConfigurationEntity.Modified`, `ConfigurationHistoryEntity.Modified`, `ConfigurationSchemaEntity.Modified`, `ConfigurationSchemaHistoryEntity.Modified`, `GenerateTemplateEntity.Modified`, `GenerateTemplateHistoryEntity.Modified`, `ViewEntity.Created`, `KeyValueEntity.Created/Modified/Deleted` (`IsDeleted => Deleted is not null`), `KeyValueVersionEntity.Modified`, `KeyValueAuditEntity.Actor: AuditActor`. The configuration, schema, template and view entities declare their stamp nullable and keep the replaced fields as nullable legacy properties (section "Stored data").
- **`LegacyAuthors`** (new, internal): `ToActor(string?)` and `ToStamp(string? author, DateTimeOffset at)`, the only place that parses the old strings. The mappings call it when the stamp is missing.
- **Versions.** A history item copies the stamp of the item it archives: `ConfigurationHistoryEntity.Modified = existing.Modified` (or the legacy stamp), instead of `_ts`. Schemas and templates the same.
- **Writes.** A configuration, schema or template write sets `Modified = AuditStamp.Of(actor, now)`; system writes (the calculation cache, key/value maintenance and rotation) use `AuditActor.System`. Key/value create sets `Created` and `Modified`, update sets `Modified` and clears `Deleted`, delete sets `Deleted`, restore clears it. The version lifetime is computed from `Modified.At`.
- **Queries.** Listings project `c.Modified` (plus `c.Author` and `c._ts` for the legacy fallback of configurations, schemas and templates). Key/value queries project `c.Modified` and `c.Deleted` and filter on `IS_DEFINED(c.Deleted)`. The versions query projects `c.Modified`.

### 4. API (`Api.Functions`, `Api.Web`)

- `GetAuthor()` is removed. `GetAuditActor()` is the only way a handler names the caller, including `GetSecretRightsAsync`, `ConfigurationHttp`, `ConfigurationSchemaHttp`, `TemplateHttp` and `ViewsHttp`.
- `GetAuditActor()` under `DEV_AUTH` returns the actor of the decoded token when the request carries one, and `System` only when it has no identity, so Debug builds record real authors as `GetAuthor()` did.
- `Api.Web` keeps building its `AuditActor` from the `ClaimsPrincipal`; it uses the new factory methods.
- The OpenAPI document changes the field types and names listed in section 1. `AuditStamp` and `AuditActor` are already components of the document.

### 5. Indexing (`ContainerFactory`)

`ApplySettings` excludes the display names from the index of organization containers, next to `/Entries/*`:

```csharp
public static readonly IReadOnlyList<string> AuditNamePaths =
[
    "/Created/By/Name/?",
    "/Modified/By/Name/?",
    "/Deleted/By/Name/?",
    "/Deletion/DeletedBy/Name/?",
    "/Actor/Name/?",
];
```

`EnsureContainerSettingsAsync` already replaces the settings of an existing container when they differ, so existing containers pick the exclusions up (an online index transformation that only drops terms).

### 6. Clients (Phase 2)

- **OpenAPI and SDKs.** Export the document with `OpenApiExportTests`, regenerate `Sdk.NSwag` and the TypeScript SDK (`npm run generate`). `Sdk.Kiota` is not regenerated, as in the previous SDK updates.
- **`sform`.** `AuditConsoleExtensions` gains `FormatStamp(AuditStamp?)` ("2026-10-14 08:00 by Ada Lovelace"). Version lists (configurations, schemas, templates, key/value pairs), the key/value listing and detail, and the view listing print the actor with `FormatActor` and the time from the stamp. The JSON output follows the API.
- **Admin UI.** `AuditActorLink.vue` moves from `components/annotations/` to `components/common/` and is used wherever an author was printed: `VersionTimeline.vue`, the configurations, schemas, templates, key/value and views pages, `KeyValueDetail.vue` (created, modified, deleted), `KeyValueVersions.vue` and `RecipientsCard.vue`. Times come from the stamps. The key/value conflict notice names the actor. The mock API (`app/mock/`) produces the new shapes. Unit, end-to-end and visual tests are updated; visual baselines are regenerated where only the rendered actor changes.

### 7. Documentation

`docs/Platform/Storyteller/access.md` (views), `annotation-lifecycle.md`, `definitions.md`, `templating.md`, `inheritance.md`, `key-values.md` and `authentication.md` describe the stamps instead of `Author`, and `audit-operations.md` gets a short section on the actor, the stamp names and the index exclusions. The user documentation in `docs/42for.net` mentions authors only in prose and stays as it is.

## Phases

| Phase | Content | Done when |
| --- | --- | --- |
| 1 | Sections 1-5 and 7: models, services, storage with the legacy reads, API, indexing, Storyteller docs, tests | `dotnet build` of the solution; `Backend.CosmosDb`, `Api.Functions` and other touched test projects green |
| 2 | Section 6: OpenAPI document, both SDKs, `sform`, admin UI | SDK tests, CLI tests, UI typecheck, lint, unit, e2e and visual tests green |

## Decisions

- **D1 — `AuditActor` with a name snapshot is the one actor type** (option C). `Kind` + `Id` is the reference; `Name` is display only and is not indexed.
- **D2 — Every who-and-when pair is an `AuditStamp`** named `Created`, `Modified` or `Deleted`. Versions carry `Modified`. `AnnotationDeletion` keeps its fields.
- **D3 — No migration.** Configuration, schema, template and view items stay readable through legacy properties and are converted on their next write. Key/value items, created on `main` the same day, change shape without a compatibility path.
- **D4 — The configuration writes always audit the annotation.** The optional actor that suppressed the entry is gone with the string author.

## Out of scope

- Resolving current display names on read, or a batch endpoint for it.
- A name for machine actors (the machine's display name is not in the request).
- Filtering listings or the audit by actor.
- The `Sdk.Kiota` client.
- Branches still in review that write an `Author` (for example 424-15, export and import of the configuration as a git repository) adapt when they are rebased.
