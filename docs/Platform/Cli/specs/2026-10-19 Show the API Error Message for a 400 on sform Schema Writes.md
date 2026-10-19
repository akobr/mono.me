# Show the API Error Message for a 400 on sform Schema Writes

Linear: [424-69 Show the API error message for a 400 on sform schema writes](https://linear.app/424fun/issue/424-69/show-the-api-error-message-for-a-400-on-sform-schema-writes)

## Problem

`sform story schema type|annotation|descendant set|edit` shows a `400` of the API as a raw dump and exits with `1` (`ERROR_CRASH`). For example, 424-59 ([Reject Descendant-Type Schemas Whose Type Cannot Descend from the Key](../../Storyteller/specs/2026-10-18%20Reject%20Descendant-Type%20Schemas%20Whose%20Type%20Cannot%20Descend%20from%20the%20Key.md)) added this rejection:

```text
sform story schema descendant set exe.s.r.c rst --import x.json
```

The output is:

```text
Error occurred: The request is not well formed, or the descendant type cannot descend from the annotation key (ErrorCode InvalidKey).

Status: 400
Response: 
```

The first line is the OpenAPI description of the `400`. NSwag puts it in the message of the `ApiException`, then adds the status and up to 512 characters of the body text. `sform` does not set `ReadResponseAsString`, so the SDK reads the body of a declared status straight into an `ErrorResponse` and keeps no text; the `Response:` line stays empty. The server's own message, "The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.", and its `ErrorCode` `InvalidKey` are in `ApiException<ErrorResponse>.Result` only, and nothing prints them. A schema the server cannot parse gets the same output. Its message is "Invalid JSON Schema: …".

The problem is older than 424-59. Every `400` of a schema write behaves this way. The exit code `1` also tells a pipeline that `sform` crashed, but the input was wrong. Other input errors of the same commands exit with `2`.

CodeRabbit wrote an analysis and a coding plan on the issue. This spec follows the plan for the code. It differs in three places: the plan treats the change as trivial and writes no spec, while this spec and a review follow the documentation workflow; the tests also cover every layer, a `400` of a read and the `401`; and `definitions.md` gets a paragraph on the output (section 4).

## Current State

All paths are relative to `src/Platform/` unless they start with `docs/`.

### The shared helper

- `Cli/src/Commands/Schemas/SchemaOperations.cs` holds the work of all schema commands. `SetAsync` serves the three `set` commands, and `EditAsync` serves the three `edit` commands, both for its read and for its save. The `get`, `delete`, `versions`, `diff` and `definition` commands use the other methods of the class.
- Each method catches a `404` itself with its own sentence and `ERROR_WRONG_INPUT` (2). `SetAsync` and the save of `EditAsync` have no such catch. Every other `ApiException` except `401` goes to the private `WriteApiError` (line 314):

  ```csharp
  private static int WriteApiError(IExtendedConsole console, ApiException exception)
  {
      if (SchemaValidationConsole.TryWrite(console, exception))
      {
          return ExitCodes.ERROR_WRONG_INPUT;
      }

      console.WriteLine($"Error occurred: {exception.Message}");
      return ExitCodes.ERROR_CRASH;
  }
  ```

- `Cli/src/Output/SchemaValidationConsole.TryWrite` prints only an `ApiException<SchemaValidationErrorResponse>` with entries in `Errors`, which is the `409` of a failed compliance check. It prints the message with `WriteImportant`, then each `annotationKey (viewName)` and its messages.
- A `401` is not caught, so `Program.Main` handles it.

### What the SDK throws for a schema write

`Storyteller/Sdk.NSwag/src/ApiSdk.g.cs`, `SetConfigurationSchemaAsync`, `SetAnnotationSchemaAsync` and `SetDescendantTypeSchemaAsync`:

| Status | Exception |
| --- | --- |
| `400` | `ApiException<ErrorResponse>` with the OpenAPI description ("The request is not well formed.", or for the descendant type the text above) |
| `401` | untyped `ApiException` |
| `403` | `ApiException<ErrorResponse>` (`AccessDenied`, `AnnotationRestricted`) |
| `409` | `ApiException<SchemaValidationErrorResponse>`, also for a concurrent change, for which the server sends an `ErrorResponse` |
| `500` | `ApiException<ErrorResponse>` |
| other, such as `413` | untyped `ApiException` "The HTTP status code of the response was not expected (…)." with the body text |

`ErrorResponse` has `Message`, `Hint`, `ErrorCode`, `Details` and `Error`. The reads and the `DELETE` of schemas declare `400` and `500` (and a `409` of the `DELETE`) as `ErrorResponse` too.

### What the server sends with a `400`

`Storyteller/Api.Functions/src/V1/ConfigurationSchemaHttp.cs`:

- A body that is not JSON: `ErrorResponse("Invalid input JSON schema: …")`, no `ErrorCode`. `sform` parses the `--import` file and the edited document before the call (`JsonInputBuilder`, `EditorService`), so it does not send such a body.
- A schema that NJsonSchema cannot parse, or an unknown type code: an `ArgumentException` from `CosmosConfigurationSchemaService` ("Invalid JSON Schema: …", "Invalid annotation type code: …") becomes `ErrorResponse(ex.Message)`, no `ErrorCode`.
- An annotation key that does not parse: `ErrorResponse("Invalid annotation key: …")`, no `ErrorCode`.
- A descendant type that cannot descend from the key: `InvalidInputException`, which `ExceptionHandlingMiddleware` answers with `400` and an `ErrorResponse` with `ErrorCode` `InvalidKey`.

### Precedents in `sform`

- `Cli/src/Commands/Configuration/ConfigPatchCommand.cs`, `WritePatchError` (line 180): `SchemaValidationConsole.TryWrite` first. Then an `ApiException<ErrorResponse>` with a message that is not blank prints the message and returns `ERROR_WRONG_INPUT` for `400` and `404`, else `ERROR_CRASH`. Then the same fallback as above.
- `Cli/src/Output/KeyValueConsoleExtensions.cs`, `WriteApiError` (line 232), used by the key/value and machine commands: prints the message with `WriteImportant` and the code on its own line as `Error code: {code}` in lowlight. Its exit codes differ: `401` and `403` give `44`, and `409` and `412` give `2`.

### Tests

`Cli/test` has no test of the schema commands. `Cli/test/Commands/AnnotationCommandFixture.cs` has the `org1`/`proj1`/`default` context and `Error(status, errorCode, message)`, which builds an `ApiException<ErrorResponse>`. `RecordingConsole` records each line as plain text. `WriteImportant` lines start with `! `. `Cli/src/Cli.csproj` makes internals visible to `42.Platform.Cli.UnitTests`.

### Documentation

`docs/Platform/Storyteller/definitions.md`, section "CLI" under the schema routes, lists the commands and their options. It does not say what `sform` prints when the API rejects a write.

## Proposed Changes

### 1. A branch for `ErrorResponse` in `SchemaOperations.WriteApiError`

`Cli/src/Commands/Schemas/SchemaOperations.cs`, `WriteApiError`. The order of the branches:

1. `SchemaValidationConsole.TryWrite`, as now. The `409` compliance output and its `ERROR_WRONG_INPUT` do not change.
2. **New.** An `ApiException<ErrorResponse>` whose `Result.Message` is not blank:
   - print `Result.Message` with `console.WriteImportant`;
   - when `Result.ErrorCode` is not blank, print `Error code: {ErrorCode}` on the next line in lowlight (`ThemedLowlight(console.Theme)`), as `KeyValueConsoleExtensions.WriteApiError` does;
   - return `ExitCodes.ERROR_WRONG_INPUT` for `400` and `404`, and `ExitCodes.ERROR_CRASH` for any other status.
3. The fallback, as now: `Error occurred: {exception.Message}` and `ERROR_CRASH`. It still covers an untyped exception, a missing `Result` and a blank message.

The sketch:

```csharp
private static int WriteApiError(IExtendedConsole console, ApiException exception)
{
    if (SchemaValidationConsole.TryWrite(console, exception))
    {
        return ExitCodes.ERROR_WRONG_INPUT;
    }

    // a declared status: the client has read the body into an ErrorResponse, so the message of the exception is only
    // the OpenAPI description of the status
    if (exception is ApiException<ErrorResponse> error
        && !string.IsNullOrWhiteSpace(error.Result?.Message))
    {
        console.WriteImportant(error.Result.Message);

        if (!string.IsNullOrWhiteSpace(error.Result.ErrorCode))
        {
            console.WriteLine($"Error code: {error.Result.ErrorCode}".ThemedLowlight(console.Theme));
        }

        return exception.StatusCode is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound
            ? ExitCodes.ERROR_WRONG_INPUT
            : ExitCodes.ERROR_CRASH;
    }

    console.WriteLine($"Error occurred: {exception.Message}");
    return ExitCodes.ERROR_CRASH;
}
```

Decisions:

- **The output.** The message comes first, with `WriteImportant`, as `SchemaValidationConsole` prints the message of a `409` in the same helper. The code comes on its own lowlight line, as the key/value and machine commands print it. CodeRabbit's analysis also proposed one line, `InvalidKey: <message>`. The two-line form is used instead, because other `sform` commands already print errors that way. `Hint` is not printed. The server sets it only from `Exception.HelpLink`, which no schema error sets, and neither precedent prints it.
- **The exit codes are those of the issue and of `WritePatchError`.** `KeyValueConsoleExtensions.WriteApiError` is not reused: its `401`/`403` → `44` and `409`/`412` → `2` mapping is not what the issue asks for. It also reads the raw body of an untyped exception, which the issue keeps out of this change.
- **No shared abstraction.** The branch is a few lines in one private helper, like `WritePatchError`. Merging the three helpers is not part of this fix.
- **Effect on the other schema commands.** `get`, `delete`, `versions`, `diff`, `definition` and the read of `edit` use the same helper. A typed error with a message that reaches it now prints the message too, with the same exit codes. A `400` (for example a key that does not parse) exits with `2` instead of `1`. A typed `403`, `409` of a `DELETE` or `500` keeps `1` and now shows the server's message. The `404` sentences of each method come before the helper and do not change. The `401` filters do not change.

### 2. Out of scope

- **A `409` of a concurrent change on a write.** The server sends an `ErrorResponse`, but the SDK declares the `409` as `SchemaValidationErrorResponse`, so the exception carries a message and no `Errors`. `TryWrite` returns `false` and the fallback prints the dump, as today. The issue is about the `400`. A fix would be a branch for an `ApiException<SchemaValidationErrorResponse>` with a message and no entries, or a change to the OpenAPI contract. It is noted as a follow-up.
- **Untyped exceptions with a body**, such as a `413` `BodyTooLarge` that the schema PUTs do not declare. They keep the fallback, as the issue says.
- **Exit code `44` for a `403`.** The schema commands have always caught the `403` themselves and returned `1`. The issue keeps `ERROR_CRASH` for any status other than `400` and `404`.
- The server, the SDKs, `ConfigPatchCommand`, `KeyValueConsoleExtensions` and the other error helpers of `sform`.

### 3. Tests

A new `Cli/test/Commands/SchemaOperationsTests.cs` (`_42.Platform.Cli.UnitTests.Commands`). It calls `SchemaOperations` directly because `WriteApiError` is private. It uses `AnnotationCommandFixture` for the console and the context, a strict `Mock<ISchemasApiClient>` and a `Mock<IEditorService>`. The typed errors are built with the constructor of `ApiException<ErrorResponse>` so that their message is the OpenAPI description, as the SDK builds them. The tests then check that the description and `Status: 400` are not in the output.

| Case | Call | Expected |
| --- | --- | --- |
| The descendant type cannot descend from the key | `SetAsync`, `Descendant`, `exe.s.r.c`, `rst`; `400` `InvalidKey` "The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there." | `! The type rst …`, `Error code: InvalidKey`, no `Error occurred:`, no OpenAPI description, `2` |
| A schema the server cannot parse | `SetAsync` for each layer (theory); `400` "Invalid JSON Schema: …", no code | `! Invalid JSON Schema: …`, no `Error code:` line, `2` |
| The save of `edit` is rejected | `EditAsync`: the read returns a schema, `EditJsonAsync` returns an edited document, the save throws the `400` | the message, `2` |
| A `404` of the save | `SetAsync`; `404` with a message | the message, `2` |
| A typed `403` or `500` with a message | `SetAsync` (theory) | the message, `1` |
| No body, or a blank message | `SetAsync`; untyped `ApiException`, and `ApiException<ErrorResponse>` with a blank message | `Error occurred: …`, `1` |
| A failed compliance check | `SetAsync`; `409` `SchemaValidationErrorResponse` with one entry | the message, `annotationKey (viewName)` and the entry's message as now, `2` |
| A `400` of a read | `GetAsync`; `400` with a message | the message, `2` |
| `401` | `SetAsync`; untyped `401` | the exception leaves the method |

Each new case for a `400` fails on the current code: it returns `1` and prints `Error occurred:`.

### 4. Documentation

`docs/Platform/Storyteller/definitions.md`, section "CLI" of the schemas: one paragraph on how `sform` reports a rejected write:

- a `400` or a `404` prints the server's message and its error code (for example `InvalidKey`) and exits with `2`;
- a failed compliance check lists each configuration that does not comply and exits with `2`;
- any other refusal prints the server's message when there is one and exits with `1`.

The public page `docs/42for.net/cli/sform.md` does not describe the schema commands, so it does not change.
