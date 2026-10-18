# Reject Descendant-Type Schemas Whose Type Cannot Descend from the Key

Linear: [424-59 Reject descendant-type schemas whose type cannot descend from the key](https://linear.app/424fun/issue/424-59/reject-descendant-type-schemas-whose-type-cannot-descend-from-the-key)

## Problem

The schema API stores a descendant-type schema for a type that can never descend from its annotation key. For example, `PUT …/configuration-schema/exe.s.r.c/type/rst` stores a responsibility schema below an execution. That layer never applies to anything: `AnnotationHierarchy.GetAncestorSchemaSources` never names an execution as an ancestor of a responsibility.

The document is still exported, as `exe.s.r.c.rst.schema.json`. An import of that archive fails the file with `InvalidKey`, so an export of a view does not round-trip cleanly. This is the known gap listed by [Export and Import of a View](2026-10-15%20Export%20and%20Import%20of%20a%20View.md) (424-40, PR #99).

The admin UI makes the mistake easy: the **New schema** dialog offers all seven type codes for a descendant-type schema, whatever the key.

## Current State

- `src/Platform/Storyteller/Abstractions.Annotations/src/AnnotationHierarchy.cs`: `GetAncestorSchemaSources(key)` lists the ancestors of a key that can hold a descendant-type schema for its type. The pairs (ancestor type → descendant type) it implies are:

  | Ancestor | Descendant types |
  | --- | --- |
  | `rst` | `unt`, `usg`, `exe`, `uxe` |
  | `sbt` | `cnt`, `usg`, `exe`, `uxe` |
  | `usg` | `exe`, `uxe` |
  | `cnt` | `exe`, `uxe` |
  | `unt` | `uxe` |
  | `exe` | `uxe` |
  | `uxe` | none |

- `src/Platform/Storyteller/Backend.Core/src/Transferring/ViewArchiveNames.cs`
  - Lines 34–39: a private `HashSet<(Ancestor, Descendant)>` of those pairs, built by calling `GetAncestorSchemaSources` on one sample key of every type (the sample needs the private `GetSegmentCount`).
  - Lines 229–233: `public static bool CanDescend(AnnotationKey ancestorKey, string descendantType)`, case-insensitive on both type codes.
  - Lines 235–253: `Descendant(...)`, used by `TryParse`, keeps an impossible pair as a document with `ErrorCode = InvalidKey` and the inline message `The type {type} cannot descend from {key}, so it has no descendant-type schema there.`
- `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationSchemaService.Import.cs`, lines 64–71: `ImportSchemaAsync` checks `ViewArchiveNames.CanDescend` and returns `ViewImportResult(Failed, ErrorCode: InvalidKey, Message: …)` with the same message, built inline a second time. The key comes from `RequireKey` (canonical, `AnnotationKey.Parse(...).ToString()`) and the type from `NormalizeAnnotationType` (lower case).
- `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationSchemaService.cs`
  - Lines 150–162: `SetDescendantTypeSchemaAsync` parses the key, normalizes the type (`NormalizeAnnotationType`, line 1008, throws `ArgumentException` for an unknown code), builds `SchemaIdentity.ForDescendant` and calls `SaveAsync`. Nothing checks the pair.
  - `SaveAsync` (line 435) validates the schema document, reads the current item and the state, checks the stored configurations (unless `force`), and writes the item, the history and the state in one batch. The import calls it too, with `isChecked: true`.
  - `GetDescendantTypeSchemaAsync`, `DeleteDescendantTypeSchemaAsync`, `GetDescendantTypeSchemaVersionsAsync`, `…VersionContentAsync` and `…VersionChangesAsync` build the same identity and never check the pair.
- `src/Platform/Storyteller/Api.Functions/src/V1/ConfigurationSchemaHttp.cs`, `SetDescendantTypeSchema` (lines 367–435): validates the key and the type code one by one, parses the body, calls the service, and catches `ArgumentException` (→ `400` without an error code), `SchemaValidationException` (→ `409`) and `SchemaConcurrencyException` (→ `409`). The `400` response uses `Definitions.Descriptions.ResponseBadRequest` ("The request is not well formed.").
- `src/Platform/Storyteller/Backend.Core/src/InvalidInputException.cs`: `InvalidInputException(message, errorCode)` derives from `StorytellerException : Exception`, not from `ArgumentException`. `Api.Functions/src/ErrorHandling/ErrorResponseMapping.cs` (line 20) maps it to `400` with its `ErrorCode`; `ExceptionHandlingMiddleware` writes that response for any handler.
- `src/Platform/Storyteller/Sdk.NSwag/src/ApiSdk.g.cs` embeds each response description as the message of the `ApiException` it throws. The newest OpenAPI snapshot is `open.api.v0.9.30.json` in `Sdk.NSwag/` and `sdk.typescript/`.
- `src/Platform/ui.admin/src/app/components/schemas/SchemaCreateDialog.vue`, line 29: `types` lists all seven codes for every kind.
- `src/Platform/ui.admin/src/app/mock/archive-names.ts`, lines 54–66: a TypeScript port of the pair table and `canDescend`, built the same way from the SDK's `getAllAncestors`. Only the archive parser of the mock uses it.
- `src/Platform/ui.admin/src/app/mock/template-schema-handlers.ts`, lines 291–299: the mock `PUT …/configuration-schema/:key/type/:type` stores any pair.
- `src/Platform/Cli/src/Commands/Schemas/SchemaDescendantSetCommand.cs` (line 28) and `SchemaDescendantEditCommand.cs` (line 33): the type argument reads "The descendant annotation type code (rst, unt, sbt, usg, cnt, exe, uxe)." `sform` has no picker; a failed write prints the `ApiException` message, which includes the response body (`Message`, `ErrorCode`).
- Docs
  - `docs/Platform/Storyteller/export-import.md`, line 32: the known gap. Lines 74, 87, 272, 354 and 405 describe the import's `InvalidKey` for such a file. Line 771 says the mock's `archive-names.ts` holds "the descendant-type check".
  - `docs/Platform/Storyteller/definitions.md`: the "Ancestor descendant-type schemas" table (lines 29–39), the `400` bullet of the HTTP API (line 165), and "Force does not change the other failure modes" (line 239).
  - `docs/42for.net/platform/configuration.md`, lines 128–136: the public table of descendants and the ancestors that may define their schema.

## Proposed Changes

### 1. One descent rule in Backend.Core

New file `src/Platform/Storyteller/Backend.Core/src/Configuring/DescendantTypeRules.cs`, namespace `_42.Platform.Storyteller.Configuring`, next to `IConfigurationSchemaService`:

```csharp
// Which annotation types descend from which (AnnotationHierarchy). A descendant-type schema exists only for such a
// pair: the schema writes reject any other, and an import fails it.
public static class DescendantTypeRules
{
    public static bool CanDescend(AnnotationKey ancestorKey, string descendantType);

    // "The type {type} cannot descend from {key}, so it has no descendant-type schema there."
    public static string GetCannotDescendMessage(AnnotationKey ancestorKey, string descendantType);

    // Throws InvalidInputException(GetCannotDescendMessage(...), ErrorCodes.InvalidKey) for a pair that cannot descend.
    public static void EnsureCanDescend(AnnotationKey ancestorKey, string descendantType);
}
```

- The pair table moves here from `ViewArchiveNames`. It is still read from `AnnotationHierarchy.GetAncestorSchemaSources`, now on one sample key per type built with the `AnnotationKey.Create*` factories (`CreateResponsibility("x")`, `CreateUnit("x", "x")`, …, `CreateUnitOfExecution("x", "x", "x", "x")`), so it does not need the segment counts of the archive names. It is never written by hand, so it cannot drift from the hierarchy.
- `CanDescend` compares both type codes case-insensitively, as today.
- The message uses the canonical key (`ancestorKey.ToString()`) and the lower-case type code, so the write and the import produce exactly the same text.
- `ViewArchiveNames` keeps no rule of its own: `Descendant(...)` calls `DescendantTypeRules.CanDescend` and `GetCannotDescendMessage`, and the public `ViewArchiveNames.CanDescend` is removed (its only callers are the import and its test, both updated).
- `CosmosConfigurationSchemaService.Import.cs` calls `DescendantTypeRules.CanDescend` and `GetCannotDescendMessage`. The import keeps returning `Failed` / `InvalidKey`; it does not throw, so one bad file never stops the rest of an archive. Older or hand-made archives still get that result.

Why not extend `ViewArchiveNames`, as the coding plan on the issue selected: the rule is about the annotation hierarchy, not about file names, and with this the schema write does not depend on the archive naming of `Transferring`. This was the alternative listed in the plan, and the recommendation of the CodeRabbit analysis on the issue. The rule cannot go to `Abstractions.Annotations`, next to `AnnotationHierarchy`, because it throws `InvalidInputException` and uses `ErrorCodes`, both in Backend.Core.

### 2. Reject an impossible pair on write

`CosmosConfigurationSchemaService.SetDescendantTypeSchemaAsync`:

```csharp
var key = AnnotationKey.Parse(annotationKey);
var typeCode = NormalizeAnnotationType(descendantTypeCode);   // an unknown code is still an ArgumentException
DescendantTypeRules.EnsureCanDescend(key, typeCode);          // InvalidInputException, InvalidKey

var identity = SchemaIdentity.ForDescendant(view, key.ToString(), typeCode);
return SaveAsync(organization, project, view, identity, schemaContent, actor, force);
```

- The check runs before `SaveAsync`: nothing is read, validated, stamped or written, and `force` does not matter. Force bypasses the compliance of stored configurations only; an impossible pair is not a compliance question.
- It runs in the service, so every caller is covered: the HTTP API, the admin UI and `sform` through it, and any other host of `IConfigurationSchemaService`.
- The reads and the delete stay lenient: `GetDescendantTypeSchemaAsync`, `DeleteDescendantTypeSchemaAsync`, the versions, the version content and the diffs do not check the pair. A document stored before this change stays readable and can be deleted. The listing and the export keep showing it until it is deleted.
- `IConfigurationSchemaService.SetDescendantTypeSchemaAsync` gets a comment that names the rule and the exception, so other implementations follow it.

### 3. HTTP API: `400` with `InvalidKey`

- `ConfigurationSchemaHttp.SetDescendantTypeSchema` gets no new catch. `InvalidInputException` is not an `ArgumentException`, so the existing `catch (ArgumentException)` does not swallow it; it reaches `ExceptionHandlingMiddleware`, which answers `400` with `ErrorResponse { Message, ErrorCode = "InvalidKey" }` through `ErrorResponseMapping`.
- `Definitions.Descriptions` gets `ResponseBadRequestDescendantType`: "The request is not well formed, or the descendant type cannot descend from the annotation key (ErrorCode InvalidKey)." It replaces `ResponseBadRequest` on the `400` of the descendant-type `PUT` only. The `GET` and `DELETE` keep the plain description, because they do not check the pair.
- Order of failures on the `PUT`: an invalid key or an unknown type code (`400` without a code, as today), then a body that is not JSON (`400`), then an impossible pair (`400`, `InvalidKey`), then a body that is not a schema (`400`), then non-compliant configurations (`409`).

### 4. OpenAPI snapshot and SDKs

The description is part of the contract, and the NSwag client embeds it as the message of its `ApiException`, so both SDKs are regenerated:

1. Export the document with `OpenApiExportTests` (`STORYTELLER_OPENAPI_EXPORT`), as in `Sdk.NSwag/README.md`, and name it after its `info.version` (`open.api.v<major>.<minor>.<patch>.json`).
2. `Sdk.NSwag/`: add the new snapshot next to the older ones, regenerate `src/ApiSdk.g.cs` with the documented `nswag openapi2csclient` command, and point the README command at the new file.
3. `sdk.typescript/`: replace `open.api.v0.9.30.json` with the new snapshot, as the previous regenerations did, and run `npm run generate`, `typecheck`, `lint`, `test` and `build`. Generated files are not edited by hand.

### 5. Admin UI

- `src/Platform/ui.admin/src/app/utils/annotation-key.ts`
  - `canDescend(ancestorKey, descendantType)` moves here from `mock/archive-names.ts`, unchanged: the pairs come from the SDK's `getAllAncestors` on one sample key per type, so there is no second hand-written table.
  - `descendantTypeCodes(ancestorKey)`: the codes that can descend from the key, in `typeCodes` order; empty for an invalid key and for a unit of execution.
- `src/Platform/ui.admin/src/app/mock/archive-names.ts` imports `canDescend` from `@/utils/annotation-key` (and re-exports it, so the port keeps the shape of `ViewArchiveNames`' callers).
- `src/Platform/ui.admin/src/app/components/schemas/SchemaCreateDialog.vue`
  - For a descendant-type schema with a valid key, `types` offers only `descendantTypeCodes(key)`. Without a valid key it offers all seven, as today (no target exists yet).
  - When the key changes and the selected type cannot descend from it, the first compatible type is selected.
  - When no type can descend (a `uxe` key), the field shows that nothing descends from the key, there is no target, and **Open editor** stays disabled.
  - `target` also requires `canDescend(key, type)`, so no route to an impossible pair can be opened from the dialog.
  - Type and annotation schemas behave as before.
- The descendant-type detail page (`pages/…/schemas/annotation/[key]/type/[type].vue`) does not change. It can still open and delete a stored document of an impossible pair; a save there gets the API's `400`, shown as any other error.
- The mock API (`mock/template-schema-handlers.ts`) mirrors the service: the `PUT` (and `POST`) of a descendant-type schema whose type cannot descend from the key answers `400` with `ErrorCode: 'InvalidKey'` and the same message, before the compliance check and whatever `force` is. Its `GET` and `DELETE` stay lenient.

### 6. `sform`

`SchemaDescendantSetCommand` and `SchemaDescendantEditCommand` (the two commands that write) describe the type argument as a type that descends from the ancestor key, list the pairs, and say that the API rejects any other type with `InvalidKey`. No validation is added to the CLI: the service is the one place that enforces the rule, and the API's message reaches the user through the existing `WriteApiError`. The read, delete, versions and diff commands keep their text, because those routes stay lenient.

### 7. Tests

- **Backend.Core** (`test/Configuring/DescendantTypeRulesTests.cs`, Shouldly):
  - `CanDescend` over the full matrix of seven ancestor types × seven descendant types (`[Theory]` + `MemberData`), against the table in Current State, plus upper-case codes.
  - The exact message for `exe.s.r.c` / `rst`, and canonical key / lower-case type in the message for upper-case input.
  - `EnsureCanDescend` throws `InvalidInputException` with `ErrorCodes.InvalidKey` and that message for an impossible pair, and does not throw for a valid one.
  - `ViewArchiveNamesTests`: the test of `ViewArchiveNames.CanDescend` moves to the new class; the parser test of `exe.s.r.c.rst.schema.json` also asserts the shared message. `ViewArchiveReaderTests` stay as they are.
- **Api.Functions** (`test/ConfigurationSchemaHttpTests.cs`):
  - `SetDescendantTypeSchema` with `exe.s.r.c` / `rst` lets `InvalidInputException` (`InvalidKey`) out of the handler instead of answering a `400` without a code.
  - Through `ExceptionHandlingMiddleware`, the same call answers `400` with `ErrorCode = InvalidKey` and the message.
  - A valid pair (`rst.payments` / `exe`) answers `200` with the schema.
  - The schema service double runs `DescendantTypeRules.EnsureCanDescend` before it records a write, like the Cosmos service, and the invalid case records none.
  - The OpenAPI document gives the descendant-type `PUT` the new `400` description, and the `GET` and `DELETE` the plain one.
- **Backend.CosmosDb** (`test/CosmosConfigurationSchemaServiceTests.cs`, Cosmos DB emulator), each test in its own project:
  - An impossible pair, with `force` false and true, throws `InvalidInputException` with `InvalidKey` and the message; afterwards the `GET` returns null, the versions are empty and the listing does not have it.
  - A valid deep pair (`exe.s.r.c` / `uxe`) is stored, read and deleted, next to the existing `rst` / `exe` test.
  - A legacy document of an impossible pair, seeded straight into the container with the id and name of `SchemaIdentity.ForDescendant`, is still read by `GetDescendantTypeSchemaAsync` and deleted by `DeleteDescendantTypeSchemaAsync`.
  - `CosmosViewImportTests` keeps its test of the import's `InvalidKey` for a hand-made file unchanged, which proves the import message did not change.
- **Admin UI** (Vitest):
  - `canDescend` and `descendantTypeCodes` over the same full matrix as the C# test, so the UI and the service cannot disagree.
  - `SchemaCreateDialog`: the descendant types offered for an `rst`, an `exe` and a `uxe` key, the reselection after a key change, and **Open editor** disabled for a `uxe` key.
  - The mock `PUT` of an impossible pair answers `400` `InvalidKey` with and without `force`, and stores nothing; a valid pair is stored.
  - The archive-name tests of the mock keep passing.

### 8. Documentation

- `docs/Platform/Storyteller/export-import.md`: remove the known-gap bullet (line 32). Where the import's `InvalidKey` is described (lines 74, 272, 354, 405), say that the schema routes reject such a schema since this change, so only an older or hand-made archive has one. Keep the example `exe.s.r.c.rst.schema.json` (line 87). Line 771: the descendant-type check of the mock now comes from `utils/annotation-key.ts`.
- `docs/Platform/Storyteller/definitions.md`: under "Ancestor descendant-type schemas", a write for any other pair is rejected with `400` `InvalidKey` before anything is stored, and the reads and the delete stay lenient for documents stored before; the `400` bullet of the HTTP API names `InvalidKey`; the list of failures that force does not change includes it.
- `docs/42for.net/platform/configuration.md`: below the table of descendants, a schema for a type that cannot descend from the annotation is rejected.

### Out of scope

- Removing or migrating existing invalid documents. They have no effect on any combined schema, and an operator can delete them through the lenient `DELETE`.
- A check of the pair on `GET`, `DELETE`, versions and diffs.
- Ancestor checks in `sform` itself.
