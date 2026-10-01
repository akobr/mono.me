# Phase A of JSON Schema revision

## Overview

Phase A of [2026-09-30 JSON Schema revision](../2026-09-30%20JSON%20Schema%20revision.md) stores each configuration schema for one view, keeps a version history and an author the way templates do, and lets a caller bypass a compliance failure with `force=true`. Legacy project-wide schema documents are not read, copied, or deleted. Phase B (SDK regeneration and the `sform` commands) is not part of this review.

`CosmosConfigurationSchemaServiceTests` passed 19 of 19 against the local Cosmos emulator.

## What Was Done

### 1. Identity and storage

Current, history, and state items for every kind share the partition `{project}.schema`. The view is part of the id, and a kind letter keeps a type listing from matching an annotation key:

| Kind | Current id | History id | State id |
| --- | --- | --- | --- |
| Type | `{view}.cfs.t.{typeCode}` | `{view}.csv.t.{typeCode}.{version}` | `{view}.css.t.{typeCode}` |
| Annotation | `{view}.cfs.a.{annotationKey}` | `{view}.csv.a.{annotationKey}.{version}` | `{view}.css.a.{annotationKey}` |
| Descendant type | `{view}.cfs.d.{descendantType}.{annotationKey}` | `{view}.csv.d.{descendantType}.{annotationKey}.{version}` | `{view}.css.d.{descendantType}.{annotationKey}` |

`EntityIdPrefixTypes` gained `ConfigurationSchemaVersion = "csv"` and `ConfigurationSchemaState = "css"`. `cfs` is unchanged. `AnnotationKey` and `Name` follow the spec: the type code for a type schema, the annotation key for an annotation schema, and the ancestor key plus `dt.{descendantType}.{annotationKey}` for a descendant-type schema.

A miss on the view-scoped id is a missing schema. Items at `cfs.{type}` in `{project}.schema`, and at `cfs.{key}` or `cfs.dt.{type}.{key}` in an annotation partition, are left in place and are not read. A view-scoped create starts at version 1. `GetAnnotationSchemaPartitionKey` was removed with the old read path.

An annotation key that is a prefix of another key can still match that other key's history rows, because the history query is `StartsWith` on `{view}.csv.a.{key}.`. Configuration history has the same limit. The kind letter stops the type code `exe` from matching an annotation key that starts with `exe.`.

### 2. Entities and models

- `ConfigurationSchemaEntity` is unchanged. `ViewName` is the view.
- `ConfigurationSchemaHistoryEntity` matches `GenerateTemplateHistoryEntity`: `Version`, `Content`, `Author`, `CreationTime`, and a 365-day `ttl`. `ConfigurationHistoryEntityExtensions.GetExpirationTime` covers it.
- `ConfigurationSchemaStateEntity` stores `LastVersion` only. There is no invalidation flag.
- `ConfigurationSchema` and `CombinedConfigurationSchema` both require `View`. Type schemas set `AnnotationType`, annotation schemas set `AnnotationKey`, and descendant-type schemas set both.
- `SchemaMappings` maps the current and history entities to `ConfigurationSchema` and `ConfigurationVersion`. The caller passes the type and key; the mapper does not infer them from `Name`.

### 3. Service surface

`IConfigurationSchemaService` takes `view` on every method and `force` on the three saves, and adds list, content, and both diff overloads for each stored kind. `IConfigurationService.CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync` take `bool force = false` as the last parameter, so existing callers keep the rejection.

`SchemaConcurrencyException` carries project, view, schema id, and the retry count.

### 4. Reads

Each get is a point read of the view-scoped id in `{project}.schema`. `GetCombinedSchemaAsync` resolves the type layer, then each ancestor descendant-type layer from `AnnotationHierarchy.GetAncestorSchemaSources`, then the annotation layer, and deep-merges them with the existing `DeepMergeSchemas`. `required` arrays are still unioned, and a later layer still overwrites other keywords. No layers means null, and `ValidateContentAsync` returns without throwing. A rejecting validation sets `ViewName` to the view argument.

### 5. Saving a schema

The three saves share one path:

1. `JsonSchema.FromJsonAsync` parses the body. Any failure becomes `ArgumentException` (`Invalid JSON Schema: ...`), including when `force` is true.
2. The current item and the state row are read with their ETags. A miss is a create.
3. When the current `Content` is deeply equal to the body, the stored schema is returned. Compliance is not checked, `Version` is not incremented, and the stored author is kept.
4. Otherwise the compliance list from section 6 is built.
5. A non-empty list with `force` false throws `SchemaValidationException`.
6. A non-empty list with `force` true is logged at warning (organization, project, view, schema id, annotation key, messages) and the write continues.
7. A create uses `(state.LastVersion or 0) + 1`, so the first document is version 1 and a create after delete continues after `LastVersion`. An update uses `existing.Version + 1`.
8. One transactional batch writes the previous version's history (updates only), creates or replaces the schema under the ETag that was read, and creates or replaces the state row. `LastVersion` is `max(previous LastVersion, allocated version)`.

The body replaces `Content`. There is no merge, `$remove`, or `$patch`.

Deviation from the template writer: an update does not take `max(current.Version, state.LastVersion)`. Step 7 of the spec says `current.Version + 1` on update. The state row still will not move backwards.

The batch retries on 409, 412, and 404, which is the template helper. The spec names 412 and a history 409; 404 covers the item disappearing between the read and the batch. The loop is one attempt plus three retries, then `SchemaConcurrencyException`.

### 6. Compliance scan

`CollectComplianceErrorsAsync` resolves the combined schema of each in-scope configuration with the unsaved document standing in for the layer being saved. Other layers are the view-scoped documents already stored.

| Save | Rows |
| --- | --- |
| Type `T` | `ProjectName`, `ViewName == view`, id starts with `{view}.cnf.{T}.` |
| Annotation `K` | `ProjectName`, id equals `{view}.cnf.{K}` |
| Descendant type `D` on ancestor `A` | `ProjectName`, `ViewName == view`, id starts with `{view}.cnf.{D}.`, then `GetAncestorSchemaSources` contains `(A, D)` |

The query stays cross-partition. Empty `Content` is skipped. Each remaining row is checked against its own combined schema, so a later layer that replaces a keyword is not a false failure, and a failure that comes from another layer is still reported. A merged document that `JsonSchema.FromJsonAsync` rejects is wrapped as `ArgumentException` and becomes HTTP 400.

### 7. Delete and version reads

Delete of a missing id returns false and does not write a state row. Delete of a present id writes that version's history, deletes the current item, and updates `LastVersion`, in one batch with the same retry as a save. The next read is a miss. Delete does not take `force`.

Version list, content, and diff follow `CosmosConfigurationTemplateService` and `JsonContentDiffer`:

- History rows for the kind's id prefix, ordered by `Version`, then the current item with `ExpirationTime = DateTimeOffset.MaxValue`.
- Content of version `N` is the history row, or the current item when its version is `N`. Unknown is null at the service and 404 at the API.
- Diff of `N` is `N-1 → N`. The two-argument method diffs any pair. Version 0 is an empty object. An unknown version throws `InvalidOperationException`, which `ToDiffResponseAsync` maps to 404 `ErrorResponse`.

Schema writes do not clear `CalculatedContent`.

### 8. Configuration writes and HTTP

`CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync` pass `key.ViewName` into `ValidateContentAsync` and skip that call when `force` is true. Empty content, a missing schema, JSON parsing, the missing-configuration error on patch, merge, and cache invalidation are unchanged.

`Definitions.Routes.ConfigurationSchema.V1` is now:

```text
v1/{org}/{project}/{view}/configuration-schema/type/{annotationType}
v1/{org}/{project}/{view}/configuration-schema/{key}
v1/{org}/{project}/{view}/configuration-schema/{key}/type/{annotationType}
v1/{org}/{project}/{view}/configuration-schema/{key}/definition
```

Each stored kind also has `/versions`, `/versions/{version}`, `/versions/{version}/diff`, and `/versions/{version}/diff/{versionFrom}`. The combined route does not. The project-only routes are gone and are not aliased.

`Definitions.Parameters.Force` is `force`. OpenAPI declares it as an optional boolean query on the three schema PUTs and on configuration PUT and PATCH. `true` is the only value treated as force; a missing or non-boolean value is false.

`ConfigurationSchemaHttp` passes the route view and that query value through. Compliance failure is 409 `SchemaValidationErrorResponse`. `SchemaConcurrencyException` is 409 `ErrorResponse`. A schema the parser rejects, and a bad type or key, stay 400. Version reads use the configuration read scopes. Writes still require `Contributor`. Configuration PUT now documents the 409 it already returned.

`docs/Platform/Storyteller/definitions.md` now describes this service and API as the current contract, and points at Phase B for the CLI.
