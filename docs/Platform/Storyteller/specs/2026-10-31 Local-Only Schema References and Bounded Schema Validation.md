# Local-Only Schema References and Bounded Schema Validation

Line numbers refer to the tree this plan was written against (`main` at `596b07c8`). No application code is changed by this document.

Linear issue [424-98](https://linear.app/424fun/issue/424-98/schema-parsing-fetches-external-dollarrefs-and-checks-patterns-without), found while building the MCP server ([424-84](https://linear.app/424fun/issue/424-84/create-mcp-interface), review "Left for later"). The issue has a CodeRabbit coding plan. This spec used it as a second opinion; section 9 lists where it differs. Before it was committed, three independent reviews (security, correctness against the code, compatibility) checked a first draft. Their findings are folded in: the cycle guard, the parse budget, the enum and `patternProperties` guards, the force rule, the options, and the definition of a local reference.

## Problem

The API parses and evaluates JSON Schemas written by project members, and any self-service user can own a project. That work trusts the schema too much.

1. **SSRF through `$ref`.** `JsonSchema.FromJsonAsync(string)` uses NJsonSchema's default reference resolver. That resolver fetches every `http(s)` `$ref` from the API's network position. A `PUT` of a schema with `"$ref": "http://10.0.0.5/…"` makes the API send that `GET` when the schema is saved. It sends it again on every configuration write that is validated against the schema. A scratch run confirmed that the request reaches a local listener.
2. **ReDoS through `pattern`.** `schema.Validate` checks `pattern`, `patternProperties` and the format regular expressions with the backtracking `Regex` engine and no match timeout. A schema with `"pattern": "^(a+)+$"` and a 40-character value can hold an API worker for hours. A per-match timeout alone does not bound a request: a value that takes 150 ms to match, repeated in a 4 MiB array, still holds a worker for hours.
3. **Other unbounded or fatal work in the same code.** These were found while planning:
   - A schema that applies itself to the same value again, such as `{"not": {"$ref": "#"}}`, parses fine. Its check recurses until the process dies of a `StackOverflowException`, which cannot be caught.
   - `{"allOf": [{"$ref": "#"}]}` and an invalid `pattern` (`"("`) also parse fine. They fail only during a check, as `InvalidOperationException` and `RegexParseException`. A configuration `PUT` or `PATCH` answers them with `500`, and an import job fails as a whole.
   - Parsing can be made quadratic: each `$ref` to a map such as `#/definitions` serializes the whole map.
   - An `enum` compares every entry with the serialized value, once per entry, inside one step of the validator.

The MCP host already refuses non-local references and sets a regular expression timeout. Direct API clients are still exposed.

## Current State

### Parsing and validation

All NJsonSchema use of the backend is in `Backend.CosmosDb/src/Configuring/CosmosConfigurationSchemaService.cs`:

- `EnsureValidSchemaAsync` (910-920) parses a schema on save and wraps any failure in `ArgumentException("Invalid JSON Schema: …")`. Its callers are:
  - `SaveAsync` (441-524), before the compliance step and before `force` is considered (453);
  - `ImportSchemaAsync` (`CosmosConfigurationSchemaService.Import.cs:88-95`), which turns `ArgumentException` into a failed file with `InvalidSchema`, also with force;
  - `IsValidSchemaAsync` (`Import.cs:269-280`), which returns `false` for `ArgumentException`. `OverlaySchemaLayers.GetAsync` (`Import.cs:289-310`) then ignores that layer of the archive.
- `ValidateAgainstSchemaAsync` (922-943) parses the combined schema with the same wrapping. It then calls `schema.Validate(content.ToString(Formatting.None))` and formats each error as `"{path}: {kind}"`. Its callers are:
  - `ValidateContentAsync` (189-213), which serves configuration create, update and PATCH (`CosmosConfigurationService.cs:412, 473, 517`, each skipped with `force` and called inside the ETag retry loop) and the configuration import (`CosmosConfigurationService.cs:723-758`, run also with `force`);
  - `CollectComplianceErrorsAsync` (646-722), which serves a schema `PUT` (474) and a schema import (`Import.cs:117`). It merges the candidate with the other stored layers (`ResolveCombinedAsync`, 724-785). It runs before `force` is considered (476), so a failure inside it is never forced.
- `DeleteAsync` (542-572) runs no parse and no check.

### Error handling

- `InvalidInputException` (`Backend.Core/src/InvalidInputException.cs`) derives from `StorytellerException`, not from `ArgumentException`. `ErrorResponseMapping.TryMap` (`Api.Functions/src/ErrorHandling/ErrorResponseMapping.cs`) maps it to `400` with its `ErrorCode`.
- The three schema `PUT` handlers (`Api.Functions/src/V1/ConfigurationSchemaHttp.cs:90-143, 241-294, 400-461`) catch `ArgumentException` (`400` without a code), `SchemaValidationException` and `SchemaConcurrencyException`. An `InvalidInputException` leaves the handler, and `ExceptionHandlingMiddleware` answers it with `400` and the code. The descendant-type `PUT` already relies on this for `InvalidKey` (444-447).
- The configuration `PUT` and `PATCH` handlers (`ConfigurationHttp.cs:321, 410`) catch `StorytellerException` and answer through `ErrorResponseMapping.ToActionResult`. Any other exception from the schema check, such as `ArgumentException("Invalid JSON Schema: …")` from a stored layer, becomes a `500`. Its body carries the exception with its stack trace (`ExceptionHandlingMiddleware.cs:39-62`).
- An import calls one service per file (`Backend.Core/src/Transferring/ViewTransferService.cs:498-524`). Only write conflicts fail a single file (474-490); any other exception fails the whole job (236-246). `ErrorCodes` (`Backend.Core/src/ErrorCodes.cs`) has `InvalidSchema` and `SchemaViolation`, described as outcomes of import files.

### The MCP host

- `Mcp/src/Understanding/LocalReferenceResolver.cs` subclasses `JsonReferenceResolver` and overrides `ResolveFileReferenceAsync` and `ResolveUrlReferenceAsync` to throw `ExternalSchemaReferenceException`.
- `SchemaPreviewer` parses with the document path `/combined-schema.json`, so that a relative `$ref` is also taken as a file reference and refused. It walks the exception chain for `ExternalSchemaReferenceException` and catches `RegexMatchTimeoutException`. It validates with a plain `parsed.Validate(json)`: no budget and no cycle guard.
- `Mcp/src/Program.cs:3-5` sets `REGEX_DEFAULT_MATCH_TIMEOUT` to 200 ms as its first statement. `Mcp/test/HostDefaultsTests.cs` checks it.
- The MCP project references no backend project, and `Backend.Core` has no NJsonSchema reference. NJsonSchema is 11.5.2 (`Directory.Packages.props`).

### The API host

`Api.Functions/src/Program.cs` sets no regular expression timeout. Besides NJsonSchema, `AnnotationsHttp.cs` (619, 697, 777 and others) builds a `Regex` from a caller's name query (`^…` or `…$`) without a timeout. `Api.Web` is a template host with no schema routes.

### NJsonSchema 11.5.2

These facts were verified in scratch programs and in the source of `JsonSchemaValidator.cs` at tag `v11.5.2`.

**References.** `ResolveReferenceAsync` resolves exactly `#` and `#/…` from the document. It sends a reference that starts with `http://` or `https://` (case-sensitive) to `ResolveUrlReferenceAsync`. Everything else goes to the file branch, including `#foo` and `#definitions/a`:

- Without a document path, the file branch fails with `NotSupportedException` ("no document path is available").
- With a document path, it calls the public virtual `ResolveFilePath(documentPath, jsonPath)`, and then `ResolveFileReferenceAsync` with an absolute path such as `C:\other.json`. An exception from `ResolveFilePath` escapes unwrapped. An exception from the URL branch arrives wrapped in `InvalidOperationException`.
- Every reference in the document is resolved on parse, used or not.

**Validation.**

- `JsonSchema.Validate(string)` is `new JsonSchemaValidator((JsonSchemaValidatorSettings?)null).Validate(json, ActualSchema)`. It reads the string with `DateParseHandling.None`. The default settings carry 13 format validators.
- The other public constructor, `JsonSchemaValidator(params IFormatValidator[])`, which an implicit `base()` binds to, has none, so it skips every `format`.
- The `protected virtual Validate(JToken, JsonSchema, SchemaType, string?, string)` receives the actual schema (`ActualSchema`) at every step. Each property, item, `additionalProperties` and `patternProperties` value, and each branch of `allOf`, `anyOf`, `oneOf` and `not`, recurses through it.
- `allOf`, `anyOf`, `oneOf` and `not` validate the **same** token again. A schema graph with a cycle through them never ends.

**Work inside one step of the validator.**

- `ValidateString` calls the static `Regex.IsMatch(value, pattern)` for a string with a `pattern`, once per type of a multi-type schema. It uses the process default timeout. `RegexMatchTimeoutException` escapes raw, with `Pattern` and `MatchTimeout`.
- `ValidatePatternProperties` matches every property not in `properties` against the `patternProperties` in order, until the first match, without recursing in between.
- `ValidateEnum` evaluates `Enumeration.All(v => v?.ToString() != token?.ToString())`. For an object or array, that serializes the value (indented) once per entry.

**Parsing.** `FromJsonAsync` observes its cancellation token once per reference.

**Speed.** A 2 MB configuration of 31,600 objects checks in 0.3–0.6 s against a moderate schema. With a 10-branch `oneOf` per item it takes 3.1 s, and with 20 branches 6.1 s, on a workstation.

## Proposed Changes

### 1. Decisions

1. **The resolver stays a local copy in `Backend.CosmosDb`.** It is about 40 lines. A shared project would hold only this class and the validator, and moving them into `Abstractions.Annotations` would add NJsonSchema to `Backend.Core`'s graph. The MCP copy stays unchanged; section 8 lists what it lacks.
2. **A local reference is `#` or a JSON Pointer `#/…`.** The two kinds of other references are refused differently:
   - one that does not start with `#` (a URL, a file, a relative path, a URN) is refused before anything is fetched or read, with the new code `ExternalSchemaReference`;
   - `#foo` or `#definitions/a` are local but unsupported, so they keep today's `InvalidSchema`, with a message that says so.
3. **Every failure of a schema is a coded `InvalidInputException`.**
   - `InvalidSchema`: the schema cannot be read, or a check finds it unusable, for example a cycle, an invalid `pattern`, or "Cyclic references detected".
   - `ExternalSchemaReference`: see the previous decision.
   - `SchemaValidationTimeout` (new): reading or checking takes too long.

   The schema `PUT` keeps `400` and gains the code `InvalidSchema`. The configuration `PUT` and `PATCH` answer `400` instead of `500`, and imports fail one file instead of the job.
4. **Stored schemas with an external reference fail closed.** A configuration write validated against such a schema answers `400 ExternalSchemaReference`. A forced write skips the check, as today. Recovery is by a forced schema `PUT` (decision 7), a `DELETE` of the layer, or an import that brings the fixed layer.
5. **Pattern matching has its own timeout, independent of the host.** The validator matches `pattern` and `patternProperties` with an explicit timeout, `ConfigurationSchemaOptions.PatternMatchTimeout`, 200 ms by default. NJsonSchema then repeats the same match, which is now known to end within the timeout. The API host also sets the process default `REGEX_DEFAULT_MATCH_TIMEOUT` to 200 ms, as the MCP host does. That default covers the format validators of NJsonSchema and the other regular expressions of the process, and it caps NJsonSchema's repeat.
6. **Reading and checking are bounded.** Bounding each match alone does not bound a request. A time budget, `ConfigurationSchemaOptions.CheckBudget` (10 s by default), applies:
   - **To a parse** through a cancellation token, which NJsonSchema checks per reference.
   - **To a check**, at every step of the validator. Each step costs at most one match timeout or one `enum` comparison within the cap below.

   Work that NJsonSchema does inside a single step gets its own guard:
   - `patternProperties` is matched first by the guard, with the deadline checked between matches;
   - an `enum` against an object or array is refused when its cost (entries × serialized length) is above a fixed cap.

   A cycle of the schema on the same value is detected exactly and answers `InvalidSchema`. A guard on the execution stack backs it up. The budget is wall-clock time, so it is an option. The default leaves about three times the slowest legitimate shape measured above.
7. **Force covers the check of the other side, never the document itself.**
   - The body of a schema write is always parsed, checked for local references, and given a trial check of `{}` (see section 4), with or without force.
   - A forced schema `PUT` stores the schema when the compliance step fails for any reason: a violation as today, or `InvalidSchema`, `ExternalSchemaReference` or `SchemaValidationTimeout` from the combined schema of a stored configuration. The failure is logged like a forced violation. This also breaks the deadlock of two stored layers that both have an external reference.
   - Without force, such a compliance failure answers `400` with its code.
   - The import does the same: a schema file is stored with a warning of that code under force, and fails without it.
   - A configuration import under force stores the document with a warning of the code, as a forced configuration `PUT` stores without a check.
8. **The HTTP surface, the routes and the OpenAPI document are unchanged.** No handler needs a code change. The OpenAPI descriptions name no schema codes besides `SchemaViolation` in the import's `force` text. Changing them would mean regenerating both SDKs for wording only, so they are left as they are on purpose. The codes are documented in `definitions.md` and `export-import.md`.

### 2. Options

**`Backend.Core/src/Configuring/ConfigurationSchemaOptions.cs`** (new), with `SectionName = "ConfigurationSchemas"`:

- `PatternMatchTimeout`: `TimeSpan`, 200 ms by default;
- `CheckBudget`: `TimeSpan`, 10 s by default.

`Backend.CosmosDb/src/EntryPoint.cs` binds and validates them, as it does for `AnnotationOptions`:

- `PatternMatchTimeout` between 10 ms and 10 s;
- `CheckBudget` between 1 s and 5 min.

`CosmosConfigurationSchemaService` takes `IOptions<ConfigurationSchemaOptions>`. It is built only through DI.

### 3. Phase 1: local-only `$ref` resolution and coded schema failures

1. **`Backend.CosmosDb/src/Configuring/LocalReferenceResolver.cs`** (new, `internal sealed`), mirroring the MCP resolver:
   - It subclasses `JsonReferenceResolver` and has `Factory`, which builds it with a `JsonSchemaAppender` and a `DefaultTypeNameGenerator`.
   - `public const string DocumentPath = "/configuration-schema.json"`. Every reference that is not `#` or `#/…` then reaches an override, instead of failing with `NotSupportedException`.
   - `ResolveFilePath(documentPath, jsonPath)` refuses before any path is built and quotes the reference as written, not a path of the API host:
     - a `jsonPath` that starts with `#` throws `NotSupportedException("The $ref '{ref}' is not supported; a reference inside the schema is '#' or a JSON Pointer ('#/…').")`, which ends as `InvalidSchema`;
     - any other `jsonPath` throws `ExternalSchemaReferenceException(jsonPath)`.
   - `ResolveUrlReferenceAsync` throws `ExternalSchemaReferenceException(url)`.
   - `ResolveFileReferenceAsync` throws the same exception with the file name only, as a fallback in case a later NJsonSchema skips `ResolveFilePath`.
2. **`Backend.CosmosDb/src/Configuring/ExternalSchemaReferenceException.cs`** (new, `internal sealed`). It carries `Reference` and has the message "The schema refers to the external document '{reference}'; only references inside the schema are resolved."
3. **`Backend.CosmosDb/src/Configuring/ConfigurationSchemaChecker.cs`** (new, `internal sealed`, built from `ConfigurationSchemaOptions` and a `TimeProvider`). It holds every NJsonSchema call of the service, so that it can be tested without Cosmos. Phase 1 adds `ParseAsync(JObject schemaContent)`:
   - It calls `JsonSchema.FromJsonAsync(json, LocalReferenceResolver.DocumentPath, LocalReferenceResolver.Factory, token)`.
   - An `ExternalSchemaReferenceException` in the exception chain (walked as in `SchemaPreviewer`) becomes `InvalidInputException("External $ref not allowed: '{reference}'. Only '#' and JSON Pointers inside the schema ('#/…') are resolved.", ExternalSchemaReference)`.
   - Any other failure becomes `InvalidInputException("Invalid JSON Schema: {message}", InvalidSchema)`. It was `ArgumentException` with the same message.
   - References and patterns quoted in messages are cut to 100 characters with `…`. A schema `PUT` can carry a `$ref` of several MiB, and the message is logged and returned.
   - `internal static bool IsCheckFailure(InvalidInputException)` is true for the three schema codes.
4. **`CosmosConfigurationSchemaService.cs`**:
   - It holds a checker built from the options and its `TimeProvider`.
   - `EnsureValidSchemaAsync` and `ValidateAgainstSchemaAsync` become instance methods that call the checker. `force` still does not reach the parse: it runs first in `SaveAsync`.
5. **`Backend.Core/src/ErrorCodes.cs`**:
   - add `ExternalSchemaReference`;
   - move `InvalidSchema` into a group of schema codes that the schema and configuration writes answer with `400` and that import files fail with.
6. **`CosmosConfigurationSchemaService.Import.cs`**:
   - `ImportSchemaAsync` catches `InvalidInputException` from `EnsureValidSchemaAsync` and fails the file with the exception's code and message, also with force.
   - `IsValidSchemaAsync` returns `false` for it, so an overlay layer with an external reference is ignored like any other invalid layer. `OverlaySchemaLayers` gets the check as a delegate, because `IsValidSchemaAsync` is no longer static.
7. **Phase 1 tests** (see section 6): references, the `InvalidSchema` code of the existing invalid-schema tests, and the service and import paths of references.

### 4. Phase 2: bounded checks

1. **`Api.Functions/src/ProcessDefaults.cs`** (new, `internal static`):
   - `RegexMatchTimeout = TimeSpan.FromMilliseconds(200)`;
   - `Apply()`, which calls `AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", RegexMatchTimeout)`.
2. **`Api.Functions/src/Program.cs`**: the first statement is `ProcessDefaults.Apply();`, with the MCP host's reason. NJsonSchema's format validators and other regular expressions use the process default match timeout, which is infinite unless it is set, and `Regex` reads it only once.

   The default applies to every `Regex` of the process without an explicit timeout, `[GeneratedRegex]` included. The generated regular expressions of the backend are anchored, simple patterns. The name queries of `AnnotationsHttp` become bounded too: a timeout there fails the request instead of holding the worker. Answering it with `400` is left for later.
3. **`ErrorCodes.SchemaValidationTimeout`**, next to `ExternalSchemaReference`.
4. **The parse budget.** `ParseAsync` passes a token from `new CancellationTokenSource(CheckBudget, timeProvider)`. An `OperationCanceledException` of that token becomes `InvalidInputException("Reading the schema takes longer than {budget}; simplify its references.", SchemaValidationTimeout)`.
5. **`ConfigurationSchemaChecker.Validate(JsonSchema schema, JObject content)`** replaces the body of `ValidateAgainstSchemaAsync` after the parse:
   - It serializes the content as today (`Formatting.None`), so date-like strings stay strings.
   - It validates against `schema.ActualSchema` with a `GuardedValidator`.
   - It returns the errors formatted as `"{path}: {kind}"`, exactly as before.
6. **`GuardedValidator`** (nested, `private sealed`). It derives from `JsonSchemaValidator` through `base((JsonSchemaValidatorSettings?)null)`, so that the 13 default format validators stay. It records the start timestamp and the path of `(token, schema)` steps, and it overrides the protected `Validate`. Before it calls the base, it checks, in order:
   1. **The stack.** `RuntimeHelpers.TryEnsureSufficientExecutionStack()` is false: the schema nests too deeply (`InvalidSchema`).
   2. **A cycle.** An entry with the same token and the same schema (by reference) is already on the path, within the run of entries for this token. Because a step depends only on the token and the schema, that repetition never ends (`InvalidSchema`, "the schema applies itself to the same value again, for example "not": { "$ref": "#" }, so its check would never end."). Only the run of the current token is scanned, because a step into a child changes the token.
   3. **The budget.** The elapsed time is over `CheckBudget` (`SchemaValidationTimeout`, "Checking the document against its schema takes longer than {budget}; simplify the schema or the document.").
   4. **`pattern`.** For a string token with a `pattern`, it matches with `Regex.IsMatch(value, pattern, RegexOptions.None, PatternMatchTimeout)`. That is the same pattern and options NJsonSchema uses, and the static overload keeps the regular expression in the framework's bounded cache.
   5. **`patternProperties`.** For an object with `patternProperties`, it takes the property names that are not in `properties` (ordinal) and matches each against the patterns in order until the first match, as NJsonSchema does. The budget is checked before each match.
   6. **`enum`.** For an object or array with an `enum`, when entries × the length of the value serialized as NJsonSchema serializes it (indented) is above 50,000,000 characters (about half a second, measured), the check would take too long (`SchemaValidationTimeout`, budget message).
7. **Mapping in `Validate`**, in addition to the guard's own failures:
   - `RegexMatchTimeoutException`, from the guard or from NJsonSchema's own match, becomes `InvalidInputException("The pattern '{pattern}' of the schema took longer than {timeout} ms to match a value; simplify the pattern.", SchemaValidationTimeout)`;
   - `InvalidOperationException` ("Cyclic references detected."), `InsufficientExecutionStackException` and `ArgumentException` (for example `RegexParseException`) become `InvalidInputException("Invalid JSON Schema: {message}", InvalidSchema)`.
8. **The trial check at save.** `EnsureValidSchemaAsync` validates `{}` against the parsed body and discards the errors. A schema whose check cannot even start, such as `{"not": {"$ref": "#"}}` or `{"allOf": [{"$ref": "#"}]}`, is refused on save with `InvalidSchema` instead of being stored. Cycles below a property are caught at check time.
9. **Force in the compliance step.**
   - `SaveAsync` catches a check failure (`IsCheckFailure`) from `CollectComplianceErrorsAsync`. With force, it logs "Schema compliance check could not finish, bypassed with force" with the code and message, and stores the schema. Without force, it rethrows (`400`).
   - `ImportSchemaAsync` catches the same. Without force, the file fails with the code and message. With force, it is stored with a warning of that code: "Force stores this schema although the configurations could not be checked: {message}".
10. **The configuration import** (`CosmosConfigurationService.cs:723-758`) catches a check failure from `ValidateContentAsync`:
    - without force, the file fails with the code and the message;
    - with force, the document is stored with a warning of that code: "Force stores the document although its schema could not be checked: {message}".

    Update the comment of `ViewImportWarning` (`Abstractions.Annotations/src/Model/ViewImportWarning.cs`), which lists the warning codes.
11. **Phase 2 tests** (see section 6).

### 5. Phase 3: documentation

- `definitions.md`:
  - **Writing a schema**: only `#` and `#/…` are resolved. Other references answer `400 ExternalSchemaReference`, `#foo` answers `400 InvalidSchema`, and a schema whose check cannot start answers `400 InvalidSchema`. All of these apply also with force.
  - **HTTP**: the codes of a `400` on the schema `PUT` and on the configuration `PUT` and `PATCH`.
  - **What is checked**: the match timeout, the budget, the cycle rule and the `enum` cap, with `400 SchemaValidationTimeout` or `InvalidSchema`. A stored schema with an external reference fails writes until it is replaced or deleted.
  - **Force**: these failures of the compliance step are forced on a schema `PUT`, and a forced configuration write skips the check.
  - A short section on the options.
- `export-import.md`: the schema import steps and the configuration validation step, the outcome table, the warnings (`ExternalSchemaReference`, `SchemaValidationTimeout`, `InvalidSchema` under force), and the `force` option row.
- `mcp.md:315`: the API refuses such a `$ref` too, and `set_schema` keeps its own check as defense in depth.
- `docs/42for.net/platform/configuration.md` (product documentation): one paragraph on what a schema may refer to and on the time limits.
- The historical files in `specs/` are not edited.

### 6. Tests

**`Backend.CosmosDb/test/ConfigurationSchemaCheckerTests.cs`** (new, no Cosmos collection):

- **External references.** A theory over `http://127.0.0.1:{port}/x.json`, `https://…#/definitions/a`, `HTTP://…`, `file:///etc/passwd`, `other.json#/definitions/a`, `/etc/passwd`, `C:/Windows/win.ini`, `//127.0.0.1/x.json` and `urn:example:a`. Each throws `ExternalSchemaReference`. The target port belongs to a loopback `TcpListener`, which receives no connection.
- **The message.** It quotes a relative reference as written, contains no path of the host, and cuts a long reference.
- **An unused external reference** inside a definition is refused too.
- **Unsupported local references.** `#foo` and `#definitions/a` throw `InvalidSchema` with the "not supported" message.
- **Local references.** `#/definitions/a` and `#` parse, and the referenced definition applies: a compliant document has no errors, a non-compliant one gets the expected `"{path}: {kind}"`.
- **A missing local target.** `#/definitions/missing` throws `InvalidSchema` ("Invalid JSON Schema: …").
- **Output.** For a schema with `format: email`, `minimum`, `enum` and `required`, the checker returns the same messages as `JsonSchema.Validate(string)`. Losing the default format validators would fail this test.
- **Pattern timeout.** With a 20 ms `PatternMatchTimeout`, `"pattern": "^(a+)+$"` and a value of 30 `a`s followed by `!` give `SchemaValidationTimeout` with the pattern and the timeout. The same applies through `patternProperties` (a long property name), and inside `items` and `oneOf`.
- **The budget.** With a `TimeProvider` that advances one second per reading, a document of many items gives `SchemaValidationTimeout`.
- **The parse budget.** With a zero `CheckBudget`, a schema with references gives `SchemaValidationTimeout`.
- **The `enum` cap.** An `enum` of 2,000 object entries against an object of about 30 KB gives `SchemaValidationTimeout` without depending on the clock. The same `enum` against a scalar passes.
- **Cycles.** `{"not": {"$ref": "#"}}`, `{"oneOf": [{"$ref": "#"}, {"type": "string"}]}` and `{"properties": {"x": {"not": {"$ref": "#/properties/x"}}}}` with `{"x": 1}` give `InvalidSchema` without a crash.
- **Unusable schemas.** `{"allOf": [{"$ref": "#"}]}` and `"pattern": "("` give `InvalidSchema`.
- **The trial check** refuses the root-level cycles.

**`Backend.CosmosDb/test/CosmosConfigurationSchemaServiceTests.cs`**:

- A type, annotation and descendant-type schema `PUT` with an external `http` reference fails with `ExternalSchemaReference`, with and without force. The listener sees no connection, nothing is stored, and a previous version stays current.
- An invalid schema `PUT` throws `InvalidInputException` with `InvalidSchema` (the existing test, which expected `ArgumentException`).
- A schema with `definitions` and an internal `$ref` saves. Configuration writes are accepted or rejected (`SchemaValidationException`) by the referenced definition.
- A stored schema with an external reference (planted as an item, as stored before this change):
  - fails a configuration create and `PATCH` with `ExternalSchemaReference`, and a forced write is stored;
  - makes the schema `PUT` of another layer fail with the code without force, and succeed with force.
- A schema with a cycle below a property saves. A configuration write that reaches the cycle then fails with `InvalidSchema`.

**`Backend.CosmosDb/test/CosmosViewImportTests.cs`**:

- **Schema files.**
  - A schema file with an external reference fails with `ExternalSchemaReference`, also with force, and stores nothing.
  - An overlay layer with an external reference is ignored. The existing `#/definitions/missing` test keeps `InvalidSchema`.
  - A schema file whose compliance step meets a stored external layer fails with the code. With force, it is stored with that warning.
- **Configuration files.** A configuration file whose stored schema has an external reference fails with `ExternalSchemaReference`. With force, it is stored with that warning.

**`Api.Functions/test`**:

- **`ConfigurationSchemaHttpTests`.** Each of the three schema `PUT` handlers lets an `InvalidInputException` with each schema code leave. Through `ExceptionHandlingMiddleware`, the type `PUT` answers `400` with the code and message.
- **Configuration writes.** A configuration `PUT` and `PATCH` whose service throws each code answers `400` with an `ErrorResponse` of that code and no exception details.
- **`ProcessDefaultsTests`.** After `ProcessDefaults.Apply()`, `AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT")` is 200 ms. The backend's patterns no longer depend on this default (decision 5), so a test of the order in `Program.cs` is not needed.

### 7. Phase boundaries and commits

1. The spec.
2. Phase 1: the resolver, the coded parse failures, the import handling and their tests.
3. Phase 2: the options, the process default, the guarded validator, the force rule, the import handling and their tests.
4. Phase 3: the documentation.
5. The review.

### 8. Left for later

- **The compliance scan multiplies a check by every affected configuration.** The budget is per check. A project owner who stores many configurations with force and then `PUT`s a slow schema still holds a worker for about (number of configurations) × the budget. The cost of the scan also grows with the view without hostile schemas, so a budget per scan, or one parse of the combined schema per scan, needs its own design.
- **Other paths multiply the budget.** A configuration write validates inside its ETag retry loop (up to four attempts), and an import validates every configuration file.
- **The MCP host.** `SchemaPreviewer` has no cycle guard and no budget. A stored schema with a cycle below a property (which the trial check at save cannot see) and a matching document can crash the MCP host. It should use the same guarded validator, ideally from a shared library that also holds the local-reference resolver.
- **Invalid patterns are found only at check time.** Refusing them on save needs a walk of the schema's patterns that does not mistake data (`default`, `examples`, `enum`) for schema keywords.
- **The name queries of `AnnotationsHttp`** answer a timeout or an invalid pattern with `500`. They should answer `400`, as the MCP host's `find_annotations` does.
- **Stored schemas that use external references today.** Before the release, a one-off query should list the schema items whose content has a `$ref` that is not `#` or `#/…`, so that their owners can be told.

### 9. Differences from the CodeRabbit coding plan

The CodeRabbit plan is close in structure: a copy of the resolver, `InvalidInputException` with new codes, the process default in `Program.cs`, and per-file import failures. This spec differs in:

1. **The document path and `ResolveFilePath`.** Without a document path, relative and scheme-less references fail as `NotSupportedException`, an uncoded `Invalid JSON Schema`. Overriding `ResolveFilePath` refuses before a path is built and quotes the reference instead of an absolute path of the host. `#foo` is kept as `InvalidSchema`.
2. **Bounded work instead of a per-match timeout only.** The plan relied on the process default of 200 ms. This spec adds:
   - the explicit match timeout of the validator;
   - a budget per parse and per check;
   - the `patternProperties` and `enum` guards;
   - the cycle guard, without which a schema crashes the worker.

   Its timeout code is `SchemaValidationTimeout` instead of `SchemaPatternTimeout`, because it covers more than patterns.
3. **Every schema failure is coded**, `InvalidSchema` included, so a configuration `PUT` or `PATCH` no longer answers `500` for a broken stored schema.
4. **Force covers the check of the other side** on schema writes and imports, and a forced configuration import stores with a warning. The plan failed both even with force.
5. **The limits are options**, because they measure wall-clock time on a shared host.
6. **Parsing and validation move to `ConfigurationSchemaChecker`**, so that the tests run real timeouts without Cosmos and without an injected validation delegate.
