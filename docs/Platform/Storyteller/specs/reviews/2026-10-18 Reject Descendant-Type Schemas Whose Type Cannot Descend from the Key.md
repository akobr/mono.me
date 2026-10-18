# Reject Descendant-Type Schemas Whose Type Cannot Descend from the Key

## Overview

The schema API no longer stores a descendant-type schema whose type can never descend from its annotation key, such as `rst` below `exe.s.r.c`. `SetDescendantTypeSchemaAsync` rejects the pair with `InvalidInputException` (`InvalidKey`) before anything is read or written, also with force. The HTTP API answers `400` with that code. One rule in Backend.Core, `DescendantTypeRules`, now serves the write, the import and the archive names. Reads, the delete, versions and diffs stay lenient, so a document stored before the check can still be read and deleted. The OpenAPI document and both SDKs, the **New schema** dialog and the mock API of the admin UI, the help of `sform` and the docs follow. This review implements [Reject Descendant-Type Schemas Whose Type Cannot Descend from the Key](../2026-10-18%20Reject%20Descendant-Type%20Schemas%20Whose%20Type%20Cannot%20Descend%20from%20the%20Key.md) in full, in one phase. Linear: [424-59](https://linear.app/424fun/issue/424-59/reject-descendant-type-schemas-whose-type-cannot-descend-from-the-key).

## What Was Done

### 1. One descent rule in Backend.Core

- New `Backend.Core/src/Configuring/DescendantTypeRules.cs` with `CanDescend`, `GetCannotDescendMessage` and `EnsureCanDescend`, as specified. The pair table is read from `AnnotationHierarchy.GetAncestorSchemaSources` on one sample key per type, built with the `AnnotationKey.Create*` factories.
- `ViewArchiveNames` lost its pair table and the public `CanDescend`. `Descendant(...)` calls the rule and its message. The import (`CosmosConfigurationSchemaService.Import.cs`) does the same. It still returns `Failed` / `InvalidKey` and does not throw.
- Why a new class instead of extending `ViewArchiveNames`, as the coding plan on the issue selected: the spec gives the reasons. The CodeRabbit analysis on the issue recommended it too.
- **The key in the message.** The message uses `ancestorKey.ToString()`. The spec calls this the canonical key, but `AnnotationKey.Parse` keeps the case of every segment, the type code included. So the key appears as given, and only the type code is lower-cased. The write, the import and the archive names all pass a parsed key and a lower-cased type, so they produce the same text. `CosmosViewImportTests` still checks the import's message unchanged, and it passes.

### 2. Reject an impossible pair on write

- `CosmosConfigurationSchemaService.SetDescendantTypeSchemaAsync` parses the key, normalizes the type (an unknown code is still an `ArgumentException`) and calls `DescendantTypeRules.EnsureCanDescend` before it builds the identity and calls `SaveAsync`. Nothing is read, validated, stamped or written, whatever `force` is.
- The method is not `async`, so the exception is thrown when the method is called, as `NormalizeAnnotationType`'s `ArgumentException` already was. Every caller awaits the call inside its `try`, so this makes no difference to them.
- `GetDescendantTypeSchemaAsync`, `DeleteDescendantTypeSchemaAsync`, versions, version content and diffs are unchanged. The listing and the export are unchanged too.
- `IConfigurationSchemaService.SetDescendantTypeSchemaAsync` has a comment naming the rule, the exception, and the lenient reads and delete.

### 3. HTTP API: `400` with `InvalidKey`

- `ConfigurationSchemaHttp.SetDescendantTypeSchema` has no new catch. `InvalidInputException` derives from `StorytellerException : Exception`, so the handler's `catch (ArgumentException)` does not take it. `ExceptionHandlingMiddleware` answers it with `400`, the message and `ErrorCode: InvalidKey`. A comment at the call says so.
- New `Definitions.Descriptions.ResponseBadRequestDescendantType`: "The request is not well formed, or the descendant type cannot descend from the annotation key (ErrorCode InvalidKey)." It is applied to the `400` of the descendant-type `PUT` only.

### 4. OpenAPI snapshot and SDKs

- `OpenApiExportTests` exported `info.version` `0.9.31.35842`. The document differs from `open.api.v0.9.30.json` only in the version and in that one description.
- `Sdk.NSwag/open.api.v0.9.31.json` was added next to the older snapshots, and the README command points at it. In the regenerated `src/ApiSdk.g.cs`, one line changed: the message that `SetDescendantTypeSchemaAsync` throws for a `400`.
- `sdk.typescript/`: `open.api.v0.9.30.json` was replaced by `open.api.v0.9.31.json`, as in the earlier regenerations. `npm run generate` changed only the version header of the generated files and set `package.json` to `0.9.31`. `typecheck`, `lint`, `test` (126 passed, 1 skipped) and `build` pass.

### 5. Admin UI

- `app/utils/annotation-key.ts` has `canDescend` (moved from the mock), `descendantTypeCodes` and an extra helper, `cannotDescendMessage`. That helper is the API's message, so the mock's archive parser and the mock `PUT` build it in one place. The pair set is still read from the SDK's `getAllAncestors`; it now iterates `annotationTypes` (`code`, `segments`) instead of calling `getAnnotationTypeInfo` per code.
- `app/mock/archive-names.ts` imports `canDescend` and `cannotDescendMessage`. Its tests were the only importer of the re-export the spec planned, and those cases moved with the rule (section 7), so the module does not re-export `canDescend`.
- `SchemaCreateDialog.vue`:
  - For a descendant-type schema with a valid key, it offers only `descendantTypeCodes(key)`, and a `watch` on that list selects the first compatible type.
  - For a `uxe` key, the select is disabled and empty, the field's help reads "No annotation type descends from {key}.", and **Open editor** stays disabled.
  - `target` also requires `canDescend(key, type)`.
  - Deviation: `type` is now `AnnotationTypeCode | undefined`; it is undefined only while nothing can descend. The kinds already shared this one ref before the change. Now the reselection also writes to it, so after a visit to the descendant kind, the type-schema select can show a different type than before, for example `rst` after a `uxe` key. Code review rated this as acceptable.
- `app/mock/template-schema-handlers.ts`: `versionedHandlers` takes an optional `reject` hook. Only the descendant route sets it. It runs in `POST` and `PUT` after the body is read, as in the API, and before force and the compliance check, and answers `400` `InvalidKey` with the API's message. `GET`, `DELETE`, versions and diffs are unchanged.
- The descendant-type detail page is unchanged, as planned.

### 6. `sform`

- The type argument of `schema descendant set` and `schema descendant edit` now lists the types that descend below each ancestor type and says that the API rejects any other type with `InvalidKey`. The read, delete, versions and diff commands keep their text.
- Follow-up, not changed here: `SchemaOperations.WriteApiError` prints any `400` of a schema write as `Error occurred:`, followed by the NSwag message, then `Status: 400` and the raw response body (with `Message` and `ErrorCode`), and exits with `ERROR_CRASH`. This was already the case for every `400` of the schema writes. `ConfigPatchCommand.WritePatchError` shows a better pattern: print `ErrorResponse.Message`, and exit with `ERROR_WRONG_INPUT`.

### 7. Tests

- **Backend.Core**
  - New `test/Configuring/DescendantTypeRulesTests.cs`:
    - `CanDescend` and `EnsureCanDescend` over the full 7 × 7 matrix (`MemberData`);
    - type codes in any case;
    - the exact message, and the lower-cased type in it;
    - the exception and its message;
    - a deep valid pair (`exe` → `uxe`).
  - In `ViewArchiveNamesTests`, the `CanDescend` test moved to the new class, and the parser test of impossible pairs now asserts the exact shared message.
  - 810 tests pass.
- **Api.Functions**: new `ConfigurationSchemaHttpTests.cs` (5 tests), on the real handler with a `DispatchProxy` schema service that runs `EnsureCanDescend` before it records a write:
  - the exception leaves the handler, and nothing is written;
  - through `ExceptionHandlingMiddleware`, the response is `400` with `ErrorCode = InvalidKey` and the message, as a `Theory` over force off and on;
  - a valid pair answers `200`;
  - the rendered OpenAPI document gives the descendant `PUT` the new description, and the `GET` and `DELETE` the plain one.

  971 tests pass.
- **Backend.CosmosDb** (`CosmosConfigurationSchemaServiceTests`, Cosmos DB emulator):
  - `DescendantTypeSchema_OfATypeThatCannotDescend_IsRejectedAndNothingIsStored` runs with force off and on. Afterwards there is no current document, no version and no listing entry. It fails without the guard and passes with it.
  - `DescendantTypeSchema_OfADeepPair_SetGetDelete` writes `uxe` below an execution, given in upper case.
  - `DescendantTypeSchema_StoredBeforeTheRule_StaysReadableAndDeletable` seeds the item that `SchemaIdentity.ForDescendant` names straight into the container. The item is read, has one version, is listed, and is deleted.
  - The whole suite passes: 415 tests.
- **Cli**: 467 tests pass; none was added, because only help text changed.
- **Admin UI** (Vitest):
  - `test/unit/utils/annotations.test.ts`: `canDescend` over the same 7 × 7 matrix, in both cases; `descendantTypeCodes` per type; an invalid key.
  - `test/unit/configuration/templates-schemas.test.ts`:
    - the dialog through a real mount: the types offered for no key and for an `rst`, an `exe` and a `uxe` key; the reselection after key changes, ending in the right route; **Open editor** disabled for a `uxe` key;
    - the mock `PUT` of an impossible pair: `400` `InvalidKey` with and without force, and nothing stored;
    - the mock `PUT` of a valid pair: the schema is stored.
  - `test/unit/transfers/archive-names.test.ts` asserts the exact message, like the C# test. Its `canDescend` cases are gone, because the matrix above covers them.
  - `npm run lint`, `npm run build` and `npm test` (784 tests) pass.

### 8. Documentation

- `export-import.md`:
  - The known-gap bullet was rewritten, not just removed, because the gap is closed only for new writes. The bullet says that the routes reject such a schema, that one stored before the check is still exported and fails the import, and what a deleted one leaves in an export: only its earlier versions, which an import ignores. Code review corrected an earlier wording that claimed deletion left it out of the export.
  - Rule 5 of the names and the first step of the schema import name `DescendantTypeRules`. They say that such a file comes from a document stored before the check, or from a hand-made archive.
  - Lines 272 and 405 were already accurate and are unchanged.
  - The mock section says where `canDescend` lives and which tests run the pairs.
- `definitions.md`: the rule under "Ancestor descendant-type schemas", the `InvalidKey` case of the `400` bullet, and the failure that force does not change.
- `docs/42for.net/platform/configuration.md`: below the table, only its pairs exist, and any other pair is rejected with `InvalidKey`.

### Code review

A review workflow ran four reviewers, one each for the backend, the HTTP contract and SDKs, the admin UI and the docs. An adversarial verifier then checked each reviewer's findings.

Two findings were confirmed, both about wording. Both are fixed:

- the import's comment and rule 5 claimed that only old or hand-made archives bring an impossible pair;
- the known-gap bullet claimed that deleting such a document leaves it out of the export.

The verifiers refuted these findings:

- the `sform` error output, which predates this change (recorded in section 6);
- the shared `type` ref of the dialog (recorded in section 5);
- the mock not validating annotation keys on the schema routes, which predates this change;
- the overview table of `using-the-platform.md`, which lists no `400` cases for any route.
