# Phase 0c of API Extensions for the Admin UI

## Overview

Phase 0c covers E5 (views), E6 (configuration listing), E7 (schema listing), E8 (template listing), E11 (name validation) and E12 (CORS) of [TypeScript SDK and API extensions for the Administration UI](../2026-10-09%20TypeScript%20SDK%20and%20API%20extensions%20for%20the%20Administration%20UI.md). E9 (labels) and E10 (search) stay deferred, as decided for views of 5,000 to 20,000 annotations.

- **Views registry:** a project now has a views registry with descriptions, and administrators can discover views that exist only in the data.
- **Listings:** configurations, schemas and templates of a view can be listed without their JSON documents.
- **Names:** new organization, project and view names follow one rule, with reserved words.
- **CORS:** documented for browser clients.

## What Was Done

### E5. Views

- **Models** (`Abstractions.Annotations/src/Model/View.cs`): `View` (`Name`, `Description`, `CreatedAt`, `CreatedBy`, `IsDefault`, `IsRegistered`), `ViewCreate` and `ViewUpdate`.
- **`IViewService`** (`Backend.Core/src/Annotating`): `GetViewsAsync(org, project, discover)`, `CreateViewAsync` and `UpdateViewAsync`.
- **`CosmosViewService`** (`Backend.CosmosDb/src/Annotating`, singleton with an injectable `TimeProvider`):
  - **Storage:** `ViewEntity` in the organization container, partition `{project}.meta`, id `view.{name}`. It deliberately has no `ViewName` property, so discovery never counts a registration as data.
  - **Listing:** registered views plus the implicit `default` (`IsRegistered = false` until it is registered). `default` comes first, then names in ordinal order.
  - **Discovery:** `SELECT DISTINCT VALUE c.ViewName FROM c WHERE STARTSWITH(c.PartitionKey, '{project}.') AND IS_DEFINED(c.ViewName)` across the organization container. It adds the unregistered views with `IsRegistered = false`.
  - **Create:** checks the name rules. `default` returns `409 ViewExists`, and a Cosmos `409` on create becomes `ConflictException` (`ViewExists`). The description is trimmed, and a blank one is stored as null.
  - **Update** works as an upsert. It registers the view when missing, keeps `CreatedAt` and `CreatedBy` of an existing registration, and replaces the description. `default` skips the name check; other names are checked.
- **HTTP** (`Api.Functions/src/V1/ViewsHttp.cs`):
  - `GET v1/{organization}/{project}/views` needs Reader, or Administrator with `?discover=true`.
  - `POST` the same route and `PUT …/views/{view}` need Administrator.
  - The functions use the `Annotations` OpenAPI tag, so no new SDK client class is needed.

### E6. Configuration listing

- **Models:** `ConfigurationSummary` (`AnnotationKey`, `AnnotationType` as the enum, `Version`, `Author`, `Hash`, `UpdatedAt`, `HasContent`) and `ConfigurationsResponse`.
- **`IConfigurationService.ListConfigurationsAsync(org, project, view, annotationType?, keyPrefix?, continuationToken?)`**, implemented in `CosmosConfigurationService`:
  - The query projects only `AnnotationKey`, `Version`, `Author`, `CalculatedContentHash` and `_ts`, plus `(IS_OBJECT(c.Content) AND ARRAY_LENGTH(ObjectToArray(c.Content)) > 0) AS HasContent`. The documents are never read.
  - It runs across the partitions of the organization container, filtered by `STARTSWITH(c.PartitionKey, '{project}.')` and `STARTSWITH(c.id, '{view}.cnf.')`.
  - The type and key-prefix filters add more `id` prefixes. One page has at most `CosmosConstants.MaxItemCountPerPage` (1000) entries, with the Cosmos continuation token.
  - The type comes from the first segment of the key; unknown codes are skipped.
- **HTTP:** `GET v1/{organization}/{project}/{view}/configurations` (Reader). An unknown `annotationType` returns `400` through the existing `AnnotationTypeValidation`.

### E7. Schema listing

- **Models:** `ConfigurationSchemaKind` (`Type`, `Annotation`, `DescendantType`) and `ConfigurationSchemaSummary`.
- **`IConfigurationSchemaService.ListSchemasAsync`:**
  - A single-partition query in `{project}.schema` for ids starting with `{view}.cfs.`, which excludes state (`css`) and history (`csv`) items.
  - The kind, type and key are parsed from the id suffix (`t.{type}`, `a.{key}`, `d.{type}.{key}`), the same identities `SchemaIdentity` builds.
  - Results are ordered by kind, then type, then key.
- **HTTP:** `GET v1/{organization}/{project}/{view}/configuration-schemas` (Reader), in a new partial file `ConfigurationSchemaHttp.Listing.cs`.

### E8. Template listing

- **Model:** `ConfigurationTemplateSummary` (`AnnotationType`, `Version`, `Author`, `UpdatedAt`).
- **`IConfigurationTemplateService.ListTemplatesAsync`:** a single-partition query in `{project}.template` for ids starting with `{view}.gen.`, which excludes `gns` and `gnv` items. Results are ordered by type code.
- **HTTP:** `GET v1/{organization}/{project}/{view}/templates` (Reader).

### E11. Name validation

- **`NameRules`** (`Backend.Core/src`):
  - The pattern `^[a-z0-9][a-z0-9-]{1,62}$`, using a generated regex.
  - The reserved organization names, and the reserved project and view names, from the spec.
  - `EnsureOrganizationName`, `EnsureProjectName` and `EnsureViewName` throw the new `InvalidInputException` with `ErrorCode` `InvalidName`.
- **`InvalidInputException`** is a `StorytellerException` that `ErrorResponseMapping` maps to `400`.
- **Applied in `CosmosAccessService.CreateAccessPointAsync`**, which `CreateAccount` also goes through:
  - The organization name is checked only when the organization is new, so organizations created before the rule keep accepting new projects.
  - The project name is always checked, because the project is always new.
- **Applied in view create and update** (except `default`).
- **Deviation:** the spec also listed "invitation keys". Invitations and members refer to existing access points, which are only looked up, so they are not checked.

### E12. CORS

- `docs/Platform/Storyteller/authentication.md` has a new section "CORS for browser clients":
  - The admin UI origin goes into the Function App's CORS settings, with an `az functionapp cors add` example.
  - No credentials are needed (bearer tokens, no cookies), and `v1/auth/configuration` must be reachable cross-origin.
  - `local.settings.json` keeps `*`.

### Deviations from the spec

1. **Codes, not enums:** `ConfigurationSchemaSummary.AnnotationType` and `ConfigurationTemplateSummary.AnnotationType` are type codes (`rst`, `exe`, …), the same as the existing `ConfigurationSchema` and `ConfigurationTemplate` models. `ConfigurationSummary.AnnotationType` is the enum, as specified.
2. **Version type:** versions are `ulong`, the same as the stored entities, instead of `long`.
3. **PUT on views** registers a missing view as well as updating its description, so the UI's "Register" action and the description editor use one call.
4. **No version bump or OpenAPI export:** the API is not bumped to 0.9 and `open.api.v0.9.x.json` is not exported in this phase. The export needs the Functions host running the new build, and the running host locks the build output. Bumping `src/Platform/version.json` also moves every Platform package, which is a release decision.

## Tests

| Project | New tests | Result |
| --- | --- | --- |
| `Api.Functions.UnitTests` | `ErrorResponseMappingTests` + `InvalidInputException` → 400 `InvalidName`. New `RouteTableTests`: no two functions share a method and route shape (parameters normalized), and the six new routes are registered. | 133 passed |
| `Backend.CosmosDb.UnitTests` (emulator) | `NameRulesTests` (valid, invalid, length, reserved per kind, no cross-over). `CosmosListingTests`: summaries without documents (`HasContent` true and false after a patch empties the document, `Hash` equal to the calculated hash, `UpdatedAt`, versions); filters by type, key prefix, view and an unknown project; schema listing of all three kinds with the current version, excluding state, history and other views; template listing with the current version, excluding other views. `CosmosViewServiceTests`: implicit `default`, register with description and author, duplicate and `default` → `ViewExists`, invalid and reserved names, update registers `default` and new views and clears descriptions, discovery of data-only views. `CosmosAccessServiceTests` +5: invalid and reserved project names, reserved new organization, a legacy organization still accepts a valid new project. | 222 passed |
| `Access.AuthKit.UnitTests` | none | 132 passed |
| `Access.Certificates.IntegrationTests` | none | 23 passed |

All 41 Platform projects in `42.mono.slnx` build. The build used a separate artifacts path because of the locked `Api.Functions` output. The test files that use nullable annotations start with `#nullable enable`, because the `Backend.CosmosDb.UnitTests` project does not turn nullable on.

## Documentation

- `docs/Platform/Storyteller/access.md`:
  - Retitled "Access Points, Views, Members and Invitations".
  - New sections "Names" and "Views".
  - `InvalidName` and `ViewExists` added to the error codes.
- `docs/Platform/Storyteller/authentication.md`: new section "CORS for browser clients".
- `docs/Platform/Storyteller/definitions.md` and `templating.md`: the listing routes added to the route tables.
- `docs/42for.net/platform/using-the-platform.md`: views registry and discovery, the name rule, and the configuration listing.

## Follow-ups

- Bump the API to 0.9, export `open.api.v0.9.x.json` into `sdk.typescript/` and `Sdk.NSwag/` from a host running this build, and regenerate `Sdk.NSwag` (companion spec, section 3).
- Phase A: the TypeScript SDK with Orval, first on v0.8.89, then on 0.9.
- Discovery runs a cross-partition `DISTINCT` over the whole organization container. If organizations grow large, the registry could also be filled on the first write to a new view.
