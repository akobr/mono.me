# Phase 2 of Restore a Previous Version

## Overview

Phase 2 of [Restore a Previous Version of a Configuration, Template or Schema](../2026-11-10%20Restore%20a%20Previous%20Version%20of%20a%20Configuration,%20Template%20or%20Schema.md) (Linear 424-221): the five restore routes of the Functions API, their OpenAPI document, and both SDKs regenerated from it. The spec's sections 5 and 6, with the API tests of section 11, and the part of section 9 that the regenerated TypeScript SDK forced: the admin UI names the new audit action.

## What Was Done

### 5. API

- **Routes and ids** (`Definitions.cs`): `Configuration.V1.VersionRestore`, `Template.V1.VersionRestore`, `ConfigurationSchema.V1.Schema{Type,Annotation,DescendantType}VersionRestore`, each `$"{…Version}/restore"`, so the schema key keeps its constraint; the operations `RestoreConfigurationVersion`, `RestoreTemplateVersion`, `RestoreSchemaVersion`, `RestoreAnnotationSchemaVersion` and `RestoreDescendantTypeSchemaVersion`. The new descriptions (`Definitions.Descriptions.RestoreVersion`, `ResponseVersionRestored`, the `…NotFound`, `…BadRequest` and `…Conflict` texts, `ForceConfigurationRestore`, `ForceSchemaRestore`) say which error code means what.
- **Handlers:**
  - `ConfigurationHttp.RestoreConfigurationVersion`: the access of `SetConfiguration` (Contributor on the annotation, `Configuration.Write`), the key, the service, `200` with `VersionRestoreResult`; a `StorytellerException` through `ErrorResponseMapping` (404 `VersionNotFound` or `AnnotationNotFound`, 409 `AnnotationDeleted` or `Conflict`, 400 for an unusable schema) and a `SchemaValidationException` as `409 SchemaValidationErrorResponse` through the new `ToSchemaConflict`.
  - `TemplateHttp.RestoreTemplateVersion`: the access of `SetTemplate`, the type code; `TemplateConcurrencyException` answers `409` with `ErrorCode` `Conflict` (the template writes answer it without a code).
  - `ConfigurationSchemaHttp.Restore.cs`: the three handlers share `RestoreVersionAsync`, with the access of the schema writes (project-wide for the type schema, on the annotation for the other two), the type and key checks, `ErrorResponseMapping` for the domain exceptions, `ArgumentException` as `400`, `SchemaValidationException` as `409` through `ToConflictResult`, and `SchemaConcurrencyException` as `409 Conflict`.
- **OpenAPI:** `200`, `400`, `401`, `403` (the scope texts of the document's write), `404`, `409` (`SchemaValidationErrorResponse` where a schema applies, `ErrorResponse` for templates) and `500`; `force` on the configuration and the three schema routes. The operations carry the document's tag.
- **Tests:**
  - `VersionRestoreHttpTests` (new, 34): what reaches each service (key, type, version, the caller, `force` from the query); every failure of the services and its status and code; the schema violations and concurrent changes as conflicts; invalid keys and type codes before the service.
  - The tables of the existing tests: `OpenApiExportTests.MachineOperations` (the 403 texts and scopes), `MachineRestrictionHttpTests.Handlers` (keyed: the configuration, annotation and descendant-type schema restores; project-wide: the template and type schema restores), `TemplateHttpTests.Writes`, `ConfigurationSchemaAuthorizationTests.Writes`, `RouteTableTests.VersionRestoreRoutes_AreRegistered` and five rows of `RouteDispatchTests` (with the `GET …/versions/3/diff` sibling of the same length).
  - All 2964 tests of `Api.Functions/test` pass.

### 6. SDKs

- The exported document is `open.api.v0.10.34.json` (`info.version` `0.10.34.56348`, the version the build stamps), added to `Sdk.NSwag` and replacing `open.api.v0.10.31.json` in `sdk.typescript`. Against 0.10.31 it adds the five paths and `VersionRestoreResult`, and changes only `AnnotationAuditEntry` (`ConfigurationRestored`, `FromVersion`) and `ConfigurationVersion` (`RestoredFrom`).
- `ApiSdk.g.cs` regenerated with NSwag 14.6.3: `RestoreConfigurationVersionAsync`, `RestoreTemplateVersionAsync`, `RestoreSchemaVersionAsync`, `RestoreAnnotationSchemaVersionAsync` and `RestoreDescendantTypeSchemaVersionAsync` on the existing clients; no new client, so no DI change.
- `sdk.typescript/src/generated` regenerated with `npm run generate` (package version `0.10.34`); `npm run typecheck` and `npm test` pass (356 tests).
- The READMEs name the new document.

### 9. Admin UI (the part the SDK forced)

The admin UI's three `Record<AnnotationAuditEntryAction, …>` maps (`app/utils/audit.ts`) did not type-check against the regenerated SDK, so this phase gave `ConfigurationRestored` its label **Configuration restored**, a colour and an icon; `auditChanges` keeps `FromVersion` (`{ kind: 'configuration', version, from }`), `AnnotationHistory.vue` links "Configuration restored as vM from vN", and the mock's set of actions that leave the Created stamp has the new one. `npm run typecheck` and `npm test` (1530 tests) pass.
