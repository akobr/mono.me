# Configuration JSON Patch CLI command

## Overview

`sform story config patch` applies one RFC 6902 document to the stored content of an existing configuration. The patch comes from the configured editor or from a file, and the command prints the version from before the call next to the version the server returned. Spec: `docs/Platform/Storyteller/specs/2026-10-04 Configuration JSON Patch CLI command.md`.

## What Was Done

### 1. Command shape

`ConfigPatchCommand` is a `BaseContextCommand` registered on `ConfigGetCommand` after `ConfigEditCommand`. `CommandNames.PATCH` is `"patch"`.

```text
sform story config patch <annotationKey> [-i|--import <file>] [-f|--force] [-y|--yes]
```

`-p|--projectKey` and `-v|--view` come from the base command. `--import` selects file mode. Omitting it selects editor mode. The editor edits the stored configuration. It does not edit the patch file.

Help (`sform storyteller config patch --help` and `sform storyteller config --help`) lists the command, the argument, and the three options. `storyteller` is the name help prints; `story` is the existing alias of that command.

### 2. Shared preparation

`StoredConfiguration` is a `readonly record struct` in its own file, `Services/StoredConfiguration.cs` (`Content`, `Version`). The spec showed the type beside the extension method. A second type in `StorytellerApiExtensions.cs` would trip SA1402, so the record has its own file.

`GetStoredConfigurationAsync` uses the same current-version lookup as the old content helper: the `GetConfigurationVersions` row `IsCurrent` selects, then `GetConfigurationVersion`. No current row, or a `404` from the version read, returns null. `Version` is the list row's `int`, widened to `long`. `GetStoredConfigurationContentAsync` now returns `stored?.Content`, so `config edit` and `config set --replace` keep their previous behavior.

A null result prints `Configuration for '{key}' does not exist. 'config patch' applies a patch to stored content and does not create a configuration.` and returns `ERROR_WRONG_INPUT`. The command does not call `SetConfigurationAsync`.

### 3. Editor mode

`EditJsonAsync` takes `bool confirm = true`. Existing callers omit it and still ask `Do you want to save these changes`. `config patch` passes `false`. The method still sets up the editor, retries invalid JSON, shows the textual diff, and returns the same exit codes for no change, abort, and a non-zero editor. With `confirm: false` it returns the edited document without that question.

The summary that describes `DetectAvailableEditors` had been sitting on `EditJsonAsync`. It now sits on `DetectAvailableEditors`.

The command builds the body with `JsonPatchBuilder.Create(stored.Content, edited)`. An empty array prints `No changes detected.` and returns `WARNING_NO_WORK_NEEDED` without calling the API. The builder's root-`replace` rule for names that contain `~` or `/` is unchanged. Editor mode does not print the `~` warning.

The patch is printed under the header `JSON Patch`, then the command asks `Apply this patch to '{key}'` unless `-y` is set. A decline prints `Patch aborted.` and returns `WARNING_ABORTED`.

### 4. File mode

`JsonPatchDocumentReader.ReadAsync` reads through `IFileSystem`.

- A missing file prints and throws `WrongInputException` with `The file '{full path}' does not exist.`
- Invalid JSON throws `WrongInputException` whose message is `Invalid JSON Patch document: ` plus the `JsonReaderException` message. Every validation failure also calls `WriteImportant` before throwing. `BaseContextCommand` returns the exit code of an `OutputException` and does not print it, which is why `JsonInputBuilder` writes the message itself. The spec required that write for the missing file; the reader does it for the other failures for the same reason.
- A top-level value other than an array uses `The patch file must be a JSON array of operations (RFC 6902).`
- Load settings ignore comments, duplicate names, and line info.
- Each element must be an object with a string `op` and a string `path`. `op` is `add`, `remove`, `replace`, `move`, `copy`, or `test`. `add`, `replace`, and `test` require a `value` property (`null` is allowed). `move` and `copy` require a string `from`. The message names the zero-based index.
- The returned `JArray` is the array that was loaded. The command sends it. `JsonPatchBuilder` does not see an imported patch.

Order of work: validate the file, load the stored configuration, then an empty array returns `No changes detected.` without a request. A missing configuration is reported even when the file is an empty array. A non-empty array with `~` in `path` or `from` prints: `A path in this patch contains '~'. The server applies JSON Pointer escapes as literal characters, so '~0' and '~1' are not decoded.` The file is still sent after confirmation. `-y` skips that question.

### 5. Version feedback

`PatchVersionReport.Create` builds the two sentences from the spec. After `200`, the command prints the message with `WriteImportant`. When `Changed` is true it then prints the saved configuration with `WriteJson` and returns `SUCCESS`. When the versions are equal it returns `WARNING_NO_WORK_NEEDED` and does not print the configuration. The arrow uses both observed numbers, including a jump of more than one.

### 6. Errors from the server

`ApiException` around `PatchConfigurationAsync`:

- `SchemaValidationConsole.TryWrite` prints the schema listing and the command returns `ERROR_WRONG_INPUT`.
- `ApiException<ErrorResponse>` with a non-empty `Result.Message` prints that message. Status `400` or `404` returns `ERROR_WRONG_INPUT`. Any other status, including `500`, returns `ERROR_CRASH`.
- Any other `ApiException` prints `Error occurred: {e.Message}` and returns `ERROR_CRASH`.

`WrongInputException` from the reader still becomes `ERROR_WRONG_INPUT` in `BaseContextCommand`.

`force` is the `bool` property, passed the same way as `config edit`. `force=false` is what `IsForce` already treats as off.

### 7. Tests

`JsonPatchDocumentReaderTests` and `PatchVersionReportTests` are in `src/Platform/Cli/test`. The reader tests use `System.IO.Abstractions.FileSystem` and a temp file, and a Moq `IExtendedConsole` so `WriteImportant` can read a theme. `Moq` was already in `Directory.Packages.props`; the test project now references it. No `MockFileSystem` package was added.

The reader tests cover an unchanged `test` plus `move` (including an extra `note` member), an empty array, a JSON object, a scalar, invalid JSON, a missing `op`, `path`, `value`, and `from` (the `from` case is index 1), an `Add` op, a file comment, and a missing file.

The report tests cover versions 4→5, 4→4, and 4→6.

`dotnet test src/Platform/Cli/test/Cli.UnitTests.csproj`: **23 passed**, 0 failed (8 existing `JsonPatchBuilder` tests plus the 15 new ones). No warnings from the new or changed CLI files. The commands were not run against a live API.

### 8. Documentation

`docs/Platform/Storyteller/definitions.md` names `config patch` next to `config set` and `config edit` in the Force section. `docs/Platform/Storyteller/templating.md` documents both invocations, the version lines, and that an imported path containing `~` is sent as written. The sentence about `JsonPatchBuilder` replacing the whole document when a generated patch would need an escaped pointer is still there.

The API, the Cosmos service, and the NSwag client were not changed.
