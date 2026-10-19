# Show the API Error Message for a 400 on sform Schema Writes

## Overview

The schema commands of `sform` now print the server's message and error code when the API refuses a write with a `400`. Before, they printed a dump of the exception and exited with `1`. `sform story schema descendant set exe.s.r.c rst --import x.json` against a type that cannot descend from the key now prints the two lines below and exits with `2`:

```text
! The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.
Error code: InvalidKey
```

A schema that the server cannot parse gets the same treatment, without the code line. The change is one branch in `SchemaOperations.WriteApiError`, which every schema command uses. This review implements [Show the API Error Message for a 400 on sform Schema Writes](../2026-10-19%20Show%20the%20API%20Error%20Message%20for%20a%20400%20on%20sform%20Schema%20Writes.md) in full, in one phase. Linear: [424-69](https://linear.app/424fun/issue/424-69/show-the-api-error-message-for-a-400-on-sform-schema-writes).

## What Was Done

### 1. A branch for `ErrorResponse` in `SchemaOperations.WriteApiError`

As specified, in `src/Platform/Cli/src/Commands/Schemas/SchemaOperations.cs`:

- `SchemaValidationConsole.TryWrite` still comes first.
- Then an `ApiException<ErrorResponse>` whose `Result.Message` is not blank:
  - the message is printed with `WriteImportant`;
  - an `ErrorCode` that is not blank is printed on the next line as `Error code: {code}` in lowlight;
  - the branch returns `ERROR_WRONG_INPUT` for `400` and `404` and `ERROR_CRASH` for any other status.
- The fallback (`Error occurred: {exception.Message}`, `ERROR_CRASH`) is unchanged.
- No using was needed. The `404` sentences of each method and the `401` filters are unchanged.

**The `404` arm cannot run today.** Code review confirmed this. The three schema PUTs of the SDK declare no `404`, so a `404` of `set` or of the save of `edit` is an untyped `ApiException` and gets the fallback with `1`. Every other method catches a `404` itself before the helper. The arm stays because the issue asks for it, and because it matches `ConfigPatchCommand.WritePatchError`. It takes effect if a write ever declares the status.

**The body reaches the branch on the real host.** One reviewer reported that the `ErrorResponse` of the SDK rejects a body with `"Hint": null`, which would send every middleware `400` to the fallback. A skeptic tested this in a scratch Functions host with the pinned worker packages and the built SDK, and refuted it. Both serializers of `Api.Functions` leave nulls out. MVC uses `JsonOptions` in `Program.cs`. The worker uses `AddCosmosDbAnnotations`, which configures `JsonSerializerOptions` with `WhenWritingNull`. With them, the SDK throws `ApiException<ErrorResponse>` with the message and `InvalidKey`. Only the API test setup (`new JsonObjectSerializer()`) writes nulls.

### 2. Out of scope

All as specified and not changed:

- the `409` of a concurrent change on `set` and `edit`;
- untyped exceptions with a body;
- the `1` for a `403`;
- the server, the SDKs and the other error helpers.

The behaviour of the first two is now pinned by tests (section 3) and described in `definitions.md` (section 4). The fix of the `409` remains a follow-up. It would need a branch for an `ApiException<SchemaValidationErrorResponse>` with a message and no entries, or a change of the OpenAPI contract.

### 3. Tests

`src/Platform/Cli/test/Commands/SchemaOperationsTests.cs` is new and has 22 cases. It uses `AnnotationCommandFixture` for the console and the context, and strict mocks of `ISchemasApiClient` and `IEditorService`. The typed errors carry the OpenAPI description as the message of the exception, as the SDK builds them. The tests check that neither the description nor `Status: 400` reaches the output.

| Case | Expected |
| --- | --- |
| `SchemaDescendantSetCommand` with `exe.s.r.c`, `rst` and `--import x.json` (a `MockFileSystem`), `400` `InvalidKey` | exactly the two lines above, `2` |
| `set` of each layer, `400` "Invalid JSON Schema: …" | the message, no code line, `2` |
| `edit` of each layer: the read returns a schema, the editor an edited document, the save throws that `400` | the message, `2` |
| `edit` of the descendant schema under `exe.s.r.c`, save refused with `InvalidKey` (a document stored before 424-59) | the message and the code, `2` |
| typed `400`, `404`, `403`, `500` with a message on `set` | the message and the code; `2`, `2`, `1`, `1` |
| an undeclared `404`, an undeclared `413` with a body, a typed exception without `Result`, a blank message | `Error occurred: …`, `1` |
| a failed compliance check | the message, `annotationKey (viewName)` and the entry, `2` |
| the `409` of a concurrent change on a write (a `SchemaValidationErrorResponse` without entries) | `Error occurred: …`, `1` |
| a `401` | the exception leaves `SetAsync`, nothing is printed |
| a `400` of `get`, the `409` of `delete` | the message; `2`, `1` |
| a `404` of `get` | the sentence of `sform`, `2` |

**Deviations from the spec.**

- The typed `404` row of the spec ("A 404 of the save") is kept, and a comment says it only pins the mapping. An undeclared `404` was added to show what a `404` of `set` really does.
- Code review added the concurrent `409` and the undeclared `404`.

With the fix reverted, the 14 cases that expect a message fail. The 8 that pin unchanged paths pass. The CLI suite passes: 692 tests.

### 4. Documentation

`docs/Platform/Storyteller/definitions.md`, the CLI section of the schemas, has a list of what `sform` prints and returns per status:

- a `400`: the message and the code, with `2`;
- a failed compliance check: the list of configurations, with `2`;
- a `404` of the reads: the sentence of `sform`, with `2`;
- a `403`, a `500` or the concurrent `409` of `delete`: the message, with `1`;
- a `401`: the general sentence of `sform`, with `44`;
- anything else, including the concurrent `409` of `set` and `edit`: the text of the error, with `1`.

The first version of the paragraph promised the message and `2` for a `404` of a write, and the message for every other refusal. Code review found both wrong, and the list now names each status. `docs/42for.net/cli/sform.md` describes no schema command and is unchanged.

### Code review

A review workflow ran four reviewers: correctness, tests, docs and spec, and consistency of `sform`. A skeptic checked each finding.

**Confirmed and fixed** (all low):

- The paragraph and the typed `404` test promised a behaviour that no schema command has. Two reviewers found this. The docs and the tests changed as described above.
- The paragraph said that every other refusal shows the server's message. That is wrong for the `401` (`44`, from `Program.Main`) and for the concurrent `409` of a write. Fixed in the docs and pinned by a test.
- The spec's Current State gives "Invalid annotation type code: …" as the `400` of an unknown type code. The handlers check the type first with `AnnotationTypeValidation.TryValidate`, which answers "Invalid annotation type: …". The message of the service cannot be reached over HTTP. The spec is left as written. The test of a `400` of `get` uses the real text.

**Refuted:**

- the `"Hint": null` body (section 1);
- a second report of the typed `404`, which the skeptic found overstated and which duplicated the first.
