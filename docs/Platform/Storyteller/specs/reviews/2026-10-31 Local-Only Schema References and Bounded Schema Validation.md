# Local-Only Schema References and Bounded Schema Validation

## Overview

The API no longer fetches or reads anything a configuration schema names. A `$ref` is resolved inside the schema only (`#` or `#/…`). Any other reference is refused before a URL or path is used, with `400 ExternalSchemaReference`.

Reading a schema and checking a document against it are now bounded:

- a pattern match has its own timeout;
- a parse and a check each have a budget;
- NJsonSchema's loops within one step of the validator are guarded;
- a schema that applies itself to the same value again is refused instead of overflowing the stack.

Every failure of a schema is a coded `400`: `InvalidSchema`, `ExternalSchemaReference` or `SchemaValidationTimeout`. So a configuration `PUT` or `PATCH` no longer answers `500` for a broken stored schema, and an import fails one file instead of the whole job. Force covers a compliance check that could not finish.

This review covers the three phases of [Local-Only Schema References and Bounded Schema Validation](../2026-10-31%20Local-Only%20Schema%20References%20and%20Bounded%20Schema%20Validation.md) and the fixes of an independent review round. Linear: [424-98](https://linear.app/424fun/issue/424-98/schema-parsing-fetches-external-dollarrefs-and-checks-patterns-without).

| Commit | Content |
|---|---|
| `9d7e0871` | The spec |
| `0cc1c898` | Phase 1: local-only references, coded parse failures, imports, tests |
| `649b81c1` | Phase 2: options, process default, guarded validator, force rule, imports, tests |
| `051ccb50` | Review round: the nullability walk, the property search cap, `multipleOf` of 0, tests |
| `7db0ea29` | Phase 3: `definitions.md`, `export-import.md`, `mcp.md`, `docs/42for.net/platform/configuration.md` |

## What Was Done

### 1. Decisions

The eight decisions of the spec are in place:

1. The resolver is a local copy in `Backend.CosmosDb`.
2. A local reference is `#` or `#/…`.
3. Every schema failure is a coded `InvalidInputException`.
4. Stored external references fail closed.
5. Pattern matching has its own timeout.
6. Reading and checking are bounded.
7. Force covers the check of the other side, never the document itself.
8. The HTTP surface and the OpenAPI document are unchanged.

The review round extended decision 6 with two more guards and a mapping (section 6).

### 2. Options

- `ConfigurationSchemaOptions` (`Backend.Core/src/Configuring/ConfigurationSchemaOptions.cs`): section `ConfigurationSchemas`, `PatternMatchTimeout` 200 ms, `CheckBudget` 10 s, with the bounds as static fields.
- `EntryPoint.AddCosmosDbAnnotations` binds and validates them (10 ms to 10 s, 1 s to 5 min), as it does `AnnotationOptions`.
- `CosmosConfigurationSchemaService` takes `IOptions<ConfigurationSchemaOptions>` and builds its `ConfigurationSchemaChecker` from it and its `TimeProvider`.

### 3. Phase 1: local-only references and coded parse failures

1. **`LocalReferenceResolver`** (`Backend.CosmosDb/src/Configuring/LocalReferenceResolver.cs`, internal):
   - `DocumentPath = "/configuration-schema.json"`, so every reference that is not `#` or `#/…` reaches an override.
   - `ResolveFilePath` refuses before any path is built. A `#…` reference that is not a pointer throws `NotSupportedException`, which ends as `InvalidSchema`. Any other reference throws `ExternalSchemaReferenceException(jsonPath)`, which quotes the reference as written.
   - `ResolveUrlReferenceAsync` refuses with the URL.
   - `ResolveFileReferenceAsync` refuses with the file name only, as a fallback.
2. **`ExternalSchemaReferenceException`** (internal) carries `Reference`. Quoted values are cut to 100 characters (`ConfigurationSchemaChecker.Shorten`).
3. **`ConfigurationSchemaChecker`** (internal) holds every NJsonSchema call. `ParseAsync` maps:
   - an external reference anywhere in the exception chain to `ExternalSchemaReference`;
   - any other failure to `InvalidSchema` with the message `Invalid JSON Schema: …`. It was `ArgumentException` with the same text.

   Deviation: in phase 1 the checker had no constructor arguments. The options came with phase 2, where they are used.
4. **`CosmosConfigurationSchemaService`**: `EnsureValidSchemaAsync` and `ValidateAgainstSchemaAsync` are instance methods over the checker.
5. **`ErrorCodes`** has a schema group (`InvalidSchema`, `ExternalSchemaReference`, later `SchemaValidationTimeout`), with a comment that writes answer them with `400` and imports fail files with them.
6. **Imports.**
   - `ImportSchemaAsync` fails a file with the exception's code and message, also with force.
   - `IsValidSchemaAsync` returns `false` for `InvalidInputException`.
   - `OverlaySchemaLayers` takes the check as a delegate, because it is no longer static.
7. **The schema `PUT`** answers `400` with `ErrorCode` `InvalidSchema` where it used to answer `400` without a code. The `InvalidInputException` leaves the handler, and `ExceptionHandlingMiddleware` maps it, so no handler changed.

### 4. Phase 2: bounded checks

1. **`ProcessDefaults`** (`Api.Functions/src/ProcessDefaults.cs`): `RegexMatchTimeout` is 200 ms, and `Apply()` sets `REGEX_DEFAULT_MATCH_TIMEOUT`. `Program.cs` calls it as its first statement.
2. **The parse budget.** `ParseAsync` passes a token from `new CancellationTokenSource(CheckBudget, timeProvider)`. Its cancellation becomes `SchemaValidationTimeout` ("Reading the schema takes longer than …").
3. **`Validate`** keeps `JsonSchema.Validate(string)`'s behaviour: the default settings through `base((JsonSchemaValidatorSettings?)null)`, so the 13 format validators stay; `ActualSchema`; and the document read back from its text. It maps:
   - `RegexMatchTimeoutException` to `SchemaValidationTimeout`, with the pattern and the timeout that was hit;
   - `InsufficientExecutionStackException`, `InvalidOperationException`, `ArgumentException` and (after the review) `ArithmeticException` to `InvalidSchema`.
4. **`GuardedValidator`** overrides the protected `Validate`, where NJsonSchema enters each value with each schema. Before it calls the base, it checks:
   1. **The stack**, with `TryEnsureSufficientExecutionStack`.
   2. **A cycle.** The same token and schema already on the path, within the run of the current token.
   3. **The budget.**
   4. **Nullability** (review round, section 6).
   5. **`pattern`**, matched with the explicit timeout.
   6. **`enum`.** Entries × serialized length above 50,000,000 is refused.
   7. **Objects only:**
      - `patternProperties`, matched first with the budget between matches;
      - the property search (review round, section 6);
      - the nullability of declared properties whose value is null (review round, section 6).
5. **The trial check at save.** `EnsureValidSchemaAsync` validates `{}` and discards the errors. A schema whose check cannot start is refused, also with force.
6. **Force in the compliance step.**
   - `SaveAsync` catches a check failure (`IsCheckFailure`). With force, it logs "Schema compliance check could not finish, bypassed with force" and stores the schema. Without force, it rethrows.
   - `ImportSchemaAsync` fails the file without force. With force, it stores the file with a warning of the code and logs as the save does.
7. **The configuration import** fails the file with the code without force, and stores it with a warning of the code with force. The comment of `ViewImportWarning` lists the new warning codes.

### 5. Phase 3: documentation

- **`definitions.md`**: new sections **References** and **Limits of a check** (with the options table), the `400` codes of the schema `PUT`, the configuration writes against an unusable schema, the force rules, and the import note.
- **`export-import.md`**: the `force` option row, the overlay rule, schema step 4 and the compliance step, configuration validation, the outcome table, and the warnings table.
- **`mcp.md`**: `set_schema`'s check is now defense in depth.
- **`docs/42for.net/platform/configuration.md`**: one paragraph on references and time limits.

The OpenAPI descriptions are left unchanged on purpose (decision 8). The `force` text of the import still names only `SchemaViolation`.

### 6. Review round

Three reviewers (security bypass, correctness, tests and docs) read the diff, and each finding was verified independently. All eight findings were confirmed, and all are fixed:

1. **The property search escaped the budget (high).** For each name of `properties` and `required` that the object lacks, NJsonSchema compares the name with every property of the object, within one step. A 60,000-name `required` against 60,000 other properties ran 27–38 s under the 10 s budget.

   `EnsurePropertySearchFits` now counts the missing names exactly (ordinal lookups) and refuses missing × properties above 50,000,000 with `SchemaValidationTimeout`. A schema whose declared properties are all present is never refused by this.
2. **`IsNullable` could still crash the worker (high).** NJsonSchema asks `IsNullable` of a declared property's schema when its value is `null`, within the parent's step and before the property's own step. `IsNullable` recurses through `oneOf`, `ActualSchema` and `ActualTypeSchema` without passing the protected `Validate`. So `{"properties":{"x":{"oneOf":[{"$ref":"#/properties/x"},…]}}}` with `{"x": null}` overflowed the stack, and a 3,000-level chain did too.

   The guard now walks `IsNullable` call for call, ported from NJsonSchema 11.5.2, with its own path and stack check. It walks it for the step's schema (`ValidateEnum` and `ValidateType` ask it at every step) and for each declared property whose value is null. A schema met again on the walk's path is a cycle (`InvalidSchema`).
3. **`IsNullable` took exponential time (high).** On a graph of `oneOf` references it took 63 s for 12 levels. The same walk counts its calls and refuses more than 100,000 with `SchemaValidationTimeout`. When the walk ends within the cap, NJsonSchema's own walk makes the same calls.
4. **`multipleOf` of 0 (medium).** It also covers `1e-30`, which is 0 as a decimal. It threw `DivideByZeroException`, uncoded. `ArithmeticException` now maps to `InvalidSchema`.
5. **Doc and test fixes (low):**
   - in the API host the pattern limit is the lower of `PatternMatchTimeout` and the 200 ms process default;
   - the trial check can time out (`SchemaValidationTimeout` on save, with a row in the outcome table);
   - replacing the broken layer itself needs no force, and the service test now replaces it without force;
   - the docs no longer promise a hard bound of exactly `CheckBudget`, but "about the budget plus one step".

A first review of the spec draft, before it was committed, had already added the cycle guard, the parse budget, the `enum` cap, the force rule, the options and the definition of a local reference.

### 7. Tests

- **`Backend.CosmosDb/test/ConfigurationSchemaCheckerTests.cs`** (new, no Cosmos, 38 cases):
  - external references of nine forms, refused with no connection to a loopback listener;
  - an unused external reference, quoting and shortening, `#foo`, and a missing target;
  - local references applied, and output parity with `JsonSchema.Validate`, format validators included;
  - real catastrophic matches with a 20 ms timeout, through `pattern`, `items`, `oneOf` and `patternProperties`, and a long pattern shortened;
  - the budget with a stepping clock, and the parse budget;
  - the `enum`, property-search and nullability caps without the clock;
  - cycles through validation and through `IsNullable`, a recursive schema over nested values that passes, and unusable schemas (`allOf` of itself, `(`, `multipleOf` 0 and `1e-30`).
- **`CosmosConfigurationSchemaServiceTests`**:
  - an external reference refused on the three schema kinds, with and without force, with nothing stored;
  - a schema with local references applied to writes;
  - a planted external layer: it fails create and `PATCH`, a forced write is stored, another layer is stored with force, and the broken layer is replaced without force;
  - root-level cycles refused on save, also with force;
  - a cycle below a property that fails the write reaching it;
  - the existing invalid-schema test now expects `InvalidSchema`.
- **`CosmosViewImportTests`**:
  - a schema file with an external reference fails, also with force;
  - an overlay layer with an external reference is ignored;
  - a schema file whose compliance step meets a stored external layer fails, and with force is stored with the warning, in a dry run and a real run;
  - a configuration file over a stored external layer fails, and with force is stored with the warning.
- **`Api.Functions/test/SchemaCheckFailureHttpTests.cs`** (new):
  - the three schema `PUT` handlers leave each code to the middleware, which answers `400` with the code and no exception;
  - configuration `PUT` and `PATCH` answer `400` with the code.
- **`ProcessDefaultsTests`** (new).

Results:

- `Backend.CosmosDb.UnitTests` 605 and `Backend.Core.UnitTests` 1,066 passed after phase 2. `Backend.CosmosDb.UnitTests` passed again after the review round: 610 of 611. The one failure, `CosmosConfigurationTemplateServiceTests.ConcurrentTemplateWrites_ProduceConsecutiveVersions`, is in the template service, which this change does not touch (an emulator response without a stream). It passed in three reruns of its class.
- `Api.Functions.UnitTests`: 2,555 passed.

Measured overhead of the guard: a warm check of a 2 MB document of 31,600 objects takes about 285 ms against 255 ms with plain NJsonSchema.

### 8. Left for later

Carried over from the spec:

- the compliance scan multiplies a check by every affected configuration, and the ETag retry loop and the configuration import multiply the budget;
- invalid patterns are found only at check time;
- the name queries of `AnnotationsHttp` answer a timeout with `500`;
- the one-off scan of stored schemas with external references before the release;
- one shared resolver and validator for the MCP host and the backend.

The review round made the MCP item more urgent. `SchemaPreviewer` validates with a plain NJsonSchema validator. So the `IsNullable` crash (a `oneOf` cycle below a property and a `null` value) and the unguarded loops of section 6 still apply to the MCP host. A stored schema with a cycle below a property passes the trial check at save, so it can reach the previewer. The MCP host should use the same `GuardedValidator`, which needs it in a library both hosts reference.
