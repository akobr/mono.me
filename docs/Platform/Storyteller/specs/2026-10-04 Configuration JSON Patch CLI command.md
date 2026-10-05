# Configuration JSON Patch CLI command

Line numbers refer to the tree this plan was written against. No application code is changed by this document.

## Problem

`PATCH /v1/{organization}/{project}/{view}/configuration/{key}` accepts a JSON Patch document (RFC 6902) and returns the stored configuration, including its new version. `sform` already calls that route from `config edit` and `config set --replace`, but only with a patch that `JsonPatchBuilder` computed from two full documents. Those operations are `add`, `remove`, and `replace`. The tool has no command that:

- reads a JSON Patch document from a file and sends that document to the server, including `move`, `copy`, and `test`;
- opens the configured editor on the stored configuration, compares the document that went into the editor with the document that came out, and sends the patch produced by that comparison;
- prints the version before the call and the version the server returned.

## Current State

### HTTP API

`PatchConfiguration` in `src/Platform/Storyteller/Api.Functions/src/V1/ConfigurationHttp.cs` (line 195) handles `PATCH` on `v1/{organization}/{project}/{view}/configuration/{key}`.

- The body is a JSON array, documented as `application/json-patch+json`. A body that is not a JSON array becomes `400` with `Invalid JSON Patch document: …`.
- The query `force=true` stores the result even when it violates the configuration schema. The default rejects that write with `409` and `SchemaValidationErrorResponse`.
- A missing configuration is `404` (`ConfigurationNotFoundException`). The route does not create a configuration.
- A patch that fails inside `JsonExtensions.ApplyPatch` is `400`. The message is `JSON Patch operation failed: …`, or `JSON Patch result is not a JSON object.` when the result is not an object.
- Success is `200` and a `Configuration`: `AnnotationKey`, `Version`, `Content`, `Author`, and the other stored fields.

`CosmosConfigurationService.PatchConfigurationAsync` (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`, line 353) applies the array to the stored `Content` of the current item.

- Equal content returns the current configuration. `Version` stays the same. No history row is written.
- Changed content validates (unless `force` is set and the document has at least one property), writes the previous version to history, and stores `Version + 1` in the same transactional batch.
- The patch runs against the item read at request time. The function does not take an `If-Match` version.

`JsonExtensions.ApplyPatch` deserializes the array with JsonPatch.Net 5.0.2. That library, as used here, does not decode JSON Pointer escapes. `remove /c~0d` does not remove `c~d`. `replace /a~1b` creates a property whose name is the characters `a~1b`. This is recorded in `docs/Platform/Storyteller/specs/reviews/2026-10-01 Phase B of Versioning of Configuration Templates.md`.

### SDK

`IConfigurationsApiClient.PatchConfigurationAsync(organization, project, view, key, force, body)` in `src/Platform/Storyteller/Sdk.NSwag/src/ApiSdk.g.cs` (line 5287) already serializes `body` as `application/json-patch+json` and sends `PATCH`. `force` is the query parameter. The CLI project references this SDK. No client regeneration is required.

The generated client maps:

| Status | Exception | Useful text |
| --- | --- | --- |
| 400, 404, 500 | `ApiException<ErrorResponse>` | `ErrorResponse.Message` (the exception's own `Message` is the generic NSwag sentence) |
| 409 | `ApiException<SchemaValidationErrorResponse>` | already printed by `SchemaValidationConsole.TryWrite` |
| 200 | `Configuration` | `Version` is `long` |

`GetConfigurationVersions` returns `ConfigurationVersion` rows whose `Version` is `int`. The current row is the one `ConfigurationVersionExtensions.IsCurrent` selects (expiration year at `DateTimeOffset.MaxValue`).

### CLI today

Command names live in `src/Platform/Cli/src/Commands/CommandNames.cs`. Configuration subcommands are registered on `ConfigGetCommand` (line 12): `set`, `edit`, `delete`, `diff`, `versions`. There is no `patch` name and no command that accepts a JSON array as its body.

`config edit` (`ConfigEditCommand.cs`) loads the stored content with `GetStoredConfigurationContentAsync`, opens it through `EditorService.EditJsonAsync`, and saves with `ReplaceConfigurationAsync`. That helper (`StorytellerApiExtensions.cs`, line 53) calls `SetConfigurationAsync` when the configuration is missing, and `PatchConfigurationAsync` when `JsonPatchBuilder.Create` produced at least one operation. The console line on success is `Configuration for '{key}' has been saved (version {saved.Version}).` The operations themselves are not printed. An empty local patch returns null and the command prints `No changes detected.` with exit code `42` (`WARNING_NO_WORK_NEEDED`).

`config set --replace` uses the same helper. `config set` without `--replace` is a merge (`POST`/`PUT` through `SetConfigurationAsync`), including the inline `$patch` property the server honors on that route. `-i|--import` on `set` is parsed by `JsonInputBuilder`, which requires a JSON object. A patch array cannot be imported there.

`JsonPatchBuilder` (`src/Platform/Cli/src/Json/JsonPatchBuilder.cs`) walks objects property by property. Arrays and scalars that differ are replaced as a whole. It emits `add`, `remove`, and `replace` only. When a changed property name contains `~` or `/`, it emits one `replace` of the root path `""` with the whole target document, so the pointer-escape limitation above is avoided for patches it generates.

`EditorService.EditJsonAsync` (`EditorService.cs`, line 138) writes the document to a temp file, opens the editor from `editor.config.json` (Visual Studio Code, Neovim, Vim, or a custom command, chosen on first use), parses the file as a JSON object, shows a textual diff, and asks `Do you want to save these changes`. It deletes the temp file afterwards. Callers that share this method are `config edit`, `template edit`, and the three schema `edit` commands.

Context (`-p|--projectKey`, `-v|--view`) comes from `BaseContextCommand`. Schema failures already use `SchemaValidationConsole`. `-f|--force` on `config set` and `config edit` is the `force` query. `docs/Platform/Storyteller/definitions.md` (the Force section) and `docs/Platform/Storyteller/templating.md` (the configuration-command paragraph) describe those two commands and the pointer limitation.

## Proposed Changes

Add `sform story config patch` (the `configuration` alias of `config` works the same way). The command applies one JSON Patch document to the stored content of an existing configuration and prints the version the server returned. It does not create a configuration, does not merge, and does not edit the calculated or resolved document.

The API, the Cosmos service, and the NSwag client stay as they are.

### 1. Command shape

New file `src/Platform/Cli/src/Commands/Configuration/ConfigPatchCommand.cs`, subclass of `BaseContextCommand`, registered next to `ConfigEditCommand` in the `[Subcommand]` list on `ConfigGetCommand`.

Add `public const string PATCH = "patch";` to `CommandNames` and use it as the command name.

```text
sform story config patch <annotationKey> [-i|--import <file>] [-f|--force] [-y|--yes]
```

| Member | Role |
| --- | --- |
| Argument 0 `AnnotationKey` | Annotation whose stored content is patched. Same description style as `config edit`. |
| `-i\|--import` | Path of a file whose top-level value is the JSON Patch array. |
| `-f\|--force` | Pass `force: true` to `PatchConfigurationAsync`. Same wording as `config edit`: store the configuration even when it violates the schema. |
| `-y\|--yes` | Skip the confirmation that asks whether to apply the patch. |

Description: `Apply a JSON Patch (RFC 6902) to the stored content of a configuration.`

`--import` selects file mode. Omitting it selects editor mode. Passing `--import` together with an attempt to open the editor does not happen: the flag chooses one mode. The editor edits the stored configuration document. It does not edit the patch file. A patch file is prepared outside the command and passed with `--import`.

Inherited options remain `-p|--projectKey` and `-v|--view`.

### 2. Shared preparation

Add `GetStoredConfigurationAsync` beside `GetStoredConfigurationContentAsync` in `StorytellerApiExtensions`. It uses the same current-version lookup and returns null in the same cases (no current row, or `404` from `GetConfigurationVersion`).

```csharp
public readonly record struct StoredConfiguration(JObject Content, long Version);

public static async Task<StoredConfiguration?> GetStoredConfigurationAsync(
    this IConfigurationsApiClient client,
    string organization,
    string project,
    string view,
    string annotationKey);
```

`Version` is the `int` from `ConfigurationVersion`, widened to `long`, so it compares with `Configuration.Version` on the patch response. `GetStoredConfigurationContentAsync` becomes a wrapper around this method so `config edit` and `config set --replace` keep their current behavior.

When the result is null, the command prints `Configuration for '{key}' does not exist. 'config patch' applies a patch to stored content and does not create a configuration.` and returns `ExitCodes.ERROR_WRONG_INPUT`. It does not call `SetConfigurationAsync`.

### 3. Editor mode

Used when `--import` is absent.

1. Load `StoredConfiguration`. On null, stop as in section 2.
2. Open that content with `EditorService.EditJsonAsync`. The file-name prefix stays `config-{annotationKey}`. `isNew` is false: this mode only runs when stored content exists, including a stored empty object.
3. Add an optional parameter `bool confirm = true` to `EditJsonAsync`. Existing callers omit it and keep today's prompt. `config patch` passes `false`. The method still configures the editor on first use, re-opens on invalid JSON, returns `WARNING_NO_WORK_NEEDED` when the parsed document is deeply equal to the input, returns `WARNING_ABORTED` when the user declines the invalid-JSON retry, returns `ERROR_CRASH` when the editor exits non-zero, and still prints the textual diff of the input and the edited document. With `confirm: false` it does not ask `Do you want to save these changes`.
4. Build the patch with `JsonPatchBuilder.Create(stored.Content, edited)`. An empty array is `No changes detected.` and `WARNING_NO_WORK_NEEDED`, and the command does not call the API. A non-empty array is the body. The builder's root-`replace` rule for names that contain `~` or `/` stays in force for this generated patch.
5. Print the patch with the header `JSON Patch` and `Console.WriteJson` on the `JArray`.
6. Unless `-y` is set, ask `Apply this patch to '{key}'` (default yes). A decline prints `Patch aborted.` and returns `WARNING_ABORTED`.
7. Call `PatchConfigurationAsync` with the array as `body` and `Force` as `force`.
8. Report the version as in section 5.

The comparison that produces the patch is `JsonPatchBuilder` on the document written to the temp file and the document read back after the editor exits. Formatting-only edits are equal and produce no operations.

The operations are computed from the snapshot taken before the editor opened. The server applies them to the stored document it reads when the request arrives. A write by someone else in between can make an operation fail or change a different value. The PATCH endpoint has no version precondition, so this command does not add one.

### 4. File mode

Used when `--import` is set.

New type `src/Platform/Cli/src/Json/JsonPatchDocumentReader.cs`. It reads through `IFileSystem`, the same abstraction as `JsonInputBuilder`.

- Missing file: `WrongInputException` with `The file '{full path}' does not exist.` after `WriteImportant`, matching `JsonInputBuilder`.
- Invalid JSON: `WrongInputException` whose message includes the `JsonReaderException` message.
- Top-level value other than an array: `The patch file must be a JSON array of operations (RFC 6902).`
- Load settings match `JsonInputBuilder`: ignore comments, ignore duplicate property names, ignore line info.
- Each element must be an object with a string `op` and a string `path`. `op` is one of `add`, `remove`, `replace`, `move`, `copy`, `test` (lowercase, as in RFC 6902). `add`, `replace`, and `test` require `value`. `move` and `copy` require a string `from`. Any other shape is `WrongInputException` naming the zero-based index and what is missing.
- The returned `JArray` keeps the file's operations, values, and extra members. The command sends that array. It does not run `JsonPatchBuilder` on an imported patch, and it does not rewrite paths.

An empty array skips the API call: `No changes detected.` and `WARNING_NO_WORK_NEEDED`.

Before sending, if any `path` or `from` contains `~`, print one warning: `A path in this patch contains '~'. The server applies JSON Pointer escapes as literal characters, so '~0' and '~1' are not decoded.` The command still sends the file after confirmation. Generated editor patches do not need this warning; `JsonPatchBuilder` already avoids escaped pointers.

Then print the array under `JSON Patch`, confirm unless `-y`, and call `PatchConfigurationAsync`. `-y` skips that question and is the switch for a script that already has a patch file.

The stored version is still loaded first so the report can name both numbers, and so a missing configuration fails before the file is applied. The file is validated before the version lookup's failure is the only error: validate the file first, then load the stored configuration, so a bad file is reported even when the key is also missing. Order:

1. Read and validate the patch file.
2. Load `StoredConfiguration`. On null, stop as in section 2.
3. Warn about `~` when present.
4. Print, confirm, send, report.

### 5. Version feedback

After `200`, compare `stored.Version` with `saved.Version`.

Changed (`saved.Version != stored.Version`):

```text
Configuration for '{key}' patched: version {stored.Version} → {saved.Version}.
```

Print that with `WriteImportant`, then `Console.WriteJson(saved)` so the new content and author are on screen. Return `ExitCodes.SUCCESS`.

Unchanged (the server returns the current row when the patched content is deeply equal, including a patch that is only successful `test` operations):

```text
Patch did not change the stored content of '{key}'. Still version {saved.Version}.
```

Return `ExitCodes.WARNING_NO_WORK_NEEDED`. Do not dump the configuration JSON.

The arrow reports the version observed before the call and the version in the response. It does not claim the increment was exactly one. Another writer can move the version by more than one between the read and the patch.

Put the two sentences in a small pure helper, `PatchVersionReport` in `src/Platform/Cli/src/Json/PatchVersionReport.cs`, so the tests can lock the text without hosting the command:

```csharp
public readonly record struct PatchVersionReport(string Message, bool Changed)
{
    public static PatchVersionReport Create(string annotationKey, long versionBefore, long versionAfter);
}
```

### 6. Errors from the server

Catch `ApiException` around the patch call.

| Case | Output | Exit code |
| --- | --- | --- |
| `SchemaValidationConsole.TryWrite` returns true | the existing schema listing | `ERROR_WRONG_INPUT` |
| `ApiException<ErrorResponse>` and `Result.Message` is non-empty | that message | `ERROR_WRONG_INPUT` for 400 and 404, `ERROR_CRASH` for anything else |
| any other `ApiException` | `Error occurred: {e.Message}` | `ERROR_CRASH` |

`WrongInputException` from the file reader already becomes `ERROR_WRONG_INPUT` through `BaseContextCommand`.

### 7. Tests

Add cases to `src/Platform/Cli/test` (xUnit and Shouldly, the existing `Cli.UnitTests` project).

`JsonPatchDocumentReaderTests`, using a `MockFileSystem` or the test project's file-system abstraction:

- a two-operation array is returned unchanged, including a `test` and a `move` with `from`;
- an empty array is accepted;
- a JSON object, a scalar, and invalid JSON each throw `WrongInputException`;
- an operation missing `op`, `path`, `value`, or `from` names its index;
- `op` value `Add` is rejected;
- comments in the file are ignored;
- a missing file throws `WrongInputException`.

`PatchVersionReportTests`:

- versions 4 and 5 produce the arrow sentence and `Changed == true`;
- versions 4 and 4 produce the "Still version 4" sentence and `Changed == false`;
- versions 4 and 6 still use the arrow, with both numbers.

`JsonPatchBuilder` stays as it is. Its tests already cover the editor-mode generator.

No new test hosts the McMaster command or calls a live API.

### 8. Documentation

Update the living docs in the same change as the command.

- `docs/Platform/Storyteller/definitions.md`, Force section: add `config patch` to the CLI commands that accept `-f|--force`.
- `docs/Platform/Storyteller/templating.md`, the paragraph that describes configuration commands: document `sform story config patch <key>`, `config patch <key> -i patch.json -y`, the editor path, the file path, the version line, and that an imported path containing `~` is sent as written. Keep the existing sentence about `JsonPatchBuilder` replacing the whole document when a generated patch would need an escaped pointer.

After implementation, add `docs/Platform/Storyteller/specs/reviews/2026-10-04 Configuration JSON Patch CLI command.md` with the review sections the repository workflow requires. Do not edit this spec to match later deviations; record those in the review.

## Out of scope

- Changing `PatchConfiguration`, `ApplyPatch`, or JsonPatch.Net's pointer decoding. Imported patches observe the current server behavior, and the command warns when a path contains `~`.
- A local preview that applies the patch on the client. The server is the applier. The response body is the result.
- Teaching `config set --import` to accept a patch array.
- Changing `config edit`. It continues to replace stored content, create a missing configuration, and hide the generated operations.
- Optimistic concurrency, `If-Match`, or retry when the version moved during the edit.
- Template or schema patch commands.
