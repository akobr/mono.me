# sform story edit

## Overview

This review covers all of [sform story edit](../2026-11-08%20sform%20story%20edit.md) (Linear 424-217). `sform story edit <key>` opens the editable content of an annotation in the configured editor and replaces the content with the document you save. It checks and normalizes the document before the diff, handles deleted keys as `sform story set` does, and reads the annotation again before the write. After the editor, any failure keeps the edit in a file. The editor service now reads the saved file as the SDK reads documents, which also fixes `config edit`, `config patch`, `template edit` and `schema … edit`. Three independent reviewers checked the implementation before it was committed. The fixes they led to are in the commits below and in the sections that follow. Nothing was run against a live API or in a terminal.

| Commit | Content |
| --- | --- |
| `1aaeb40a` | The spec |
| `912b7ae5` | The editor service reads the saved file with `DateParseHandling.None`, 256 levels and duplicate properties as an error; safe temporary file names; `OpenFileInEditorAsync` virtual; `EditorServiceTests` and `ScriptedEditor` |
| `ba39cbc6` | The check of `EditJsonAsync` (`JsonEditCheck`) |
| `a16684cc` | Values that are not JSON (`undefined`, constructors, `NaN`, `Infinity`) re-open the editor; the diff goes through `ShowChanges` |
| `3edbab76` | `StorytellerEditCommand`, `AnnotationEditDocument`, `AnnotationKeyCasing`, the registration, and their tests |
| `edbf5102` | The documentation |
| This commit | This review |

## What Was Done

### 1. `StorytellerEditCommand`

`src/Platform/Cli/src/Commands/Storyteller/StorytellerEditCommand.cs`, registered after `StorytellerSetCommand` in `StorytellerListCommand`. Its help is "Edit the content of an annotation in your preferred editor. The saved document replaces the content." It has no `--force`. The constructor takes `IFileSystem` as well, for the kept edit.

The flow follows the spec, with these differences:

- **The interactive console** (step 2) is refused when the input **or the output** is redirected. A reviewer read Sharprompt 3.2's `DefaultConsoleDriver`: it throws for either, and with only the output redirected the editor opened and the save question threw, losing the edit. The message says "without redirected input or output", and the exit code is `WARNING_INTERACTION_NEEDED` (43), as planned.
- **The deleted record** (step 4) comes from `TryGetDeletedAnnotationAsync`, as planned. When it is gone (purged after the lookup), the edit starts from an empty document as new.
- **Nothing to save** (step 6): when both apply (**Restore and edit** below a deleted ancestor, with a key that does not exist yet), both hints are printed, the one about the restore and the one about the create. The plan had them as alternatives.
- **The save** (step 7):
  - **The body is built first**, before the guard and the restore. A body that cannot be built then fails before anything has changed.
  - **The guard** compares the editable document of the new read with the one you started from. A change of the relations or the stamps alone, such as a child created under the key, is therefore no change. An annotation that went away (deleted) or appeared (created) is a change.
  - **A partial restore.** The server restores the keys one by one and keeps what it restored when a later key fails. The command prints "Restored <keys>." for those as well, and the kept edit then says that the restored annotations keep their deleted content.
  - **The kept edit** goes to a new file in the temporary folder, named as the editor's files are. It is written without a BOM, and the hint quotes the path (`-i "<path>"`), which may contain spaces. If the file cannot be written, the command prints "Your edit could not be kept in a file: …" and keeps the outcome it was reporting.
- **The errors** are as in the table. `WriteFailure` maps them the same way for the reads before the editor and for the save. A `401`, a network error or any other exception after the editor is re-thrown after the edit is kept.
- **Precondition seams for 424-210:** `ReadActiveAsync`, `HasChangedAsync`, `RestoreAsync` and `WriteAsync` are small private methods, so the follow-up of section 8 can swap them for the versioned SDK calls.

### 2. `AnnotationEditDocument`

`src/Platform/Cli/src/Model/AnnotationEditDocument.cs`. It has `Create`, `Validate`, `Normalize`, `Check` and `ToAnnotation`, as planned.

- **The members** and their order are those of `ViewAnnotationDocument`. The non-editable members are the other public properties of the key's core runtime type (`AnnotationTypes.GetRuntimeType`), so `AnnotationType`, `ProjectName`, the relation keys and arrays, `Created`, `Modified` and `Deletion` are refused with the planned message. `AdditionalProperties` of the SDK model is an unknown member. The message for `UnitType` and `UnitDefinition` on another type names the member in its canonical case.
- **JSON nulls.** A `null` is written as `JValue.CreateNull()`. `new JValue((string)null)` is of type `String` and would never deep-equal the parsed file.
- **The dates** are accepted when System.Text.Json's `TryGetDateTimeOffset` reads them and they end with a time and an explicit offset: `Z`, `+hh:mm`, or `+hh`, which the API also reads. A reviewer found that the first draft rejected `+hh`. The pattern requires the time, so a date alone such as `2026-12-01`, whose end looks like an offset, is still refused. `Normalize` writes them as `Create` does (`yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz`).
- **The rules** of `AnnotationRules.GetProblems` are repeated with its wording and `AnnotationLimits`: the lengths of the title, description and documentation link, and an absolute http or https link without white space.
- **`ToAnnotation`** reads the document with a serializer that has `MetadataPropertyHandling.Ignore` and `DateParseHandling.None`, not with `JsonSerializer.CreateDefault()` as `set` reads a file. A reviewer showed that the default serializer treats a leading `$ref`, `$id`, `$type` or `$values` in a value as Newtonsoft metadata. A value the user confirmed was then sent changed (`{"$ref": …}` as `null`), or could not be built at all.

### 3. `EditorService`

- **The check**, `Func<JObject, JsonEditCheck>? check`, is the last optional parameter of `EditJsonAsync`. `JsonEditCheck` is a record with `Valid` and `Invalid`. A missing problem on an invalid check is shown as "The document is not valid."
- **The reader** (`ParseDocument`, `internal static`) is as planned: `DateParseHandling.None`, `MaxDepth = 256`, `DuplicatePropertyNameHandling.Error`, comments ignored, then `while (reader.Read())`. In addition, it refuses what Newtonsoft reads beyond JSON (`undefined`, constructors such as `new Date(1)`, `NaN`, `Infinity`): "The value at '<path>' is not JSON (…)". A reviewer showed that these passed the check and were sent changed, or failed after the restore. This applies to every caller.
- **The temporary file name** (`CreateTempFilePath`, `internal static`) keeps ASCII letters and digits, `.`, `-` and `_` (`char.IsAsciiLetterOrDigit`). The plan said "letters", so a non-ASCII letter is now `_` too. That is safer on a command line and only affects the name of a temporary file.
- **`OpenFileInEditorAsync`** is `virtual`, and the header and diff go through a new `protected virtual ShowChanges`, so `ScriptedEditor` (`test/Services/ScriptedEditor.cs`) can stand in for the editor and see the diff.
- The Moq setups of `TemplateCommandsTests` and `SchemaOperationsTests` needed no change: Moq matches the omitted `check` as `null`.

### 4. The key in lower case

`AnnotationKeyCasing.WithLowerCaseTypeCode` in `src/Platform/Cli/src/Model/AnnotationKeyCasing.cs`, as planned. `StorytellerSetCommand` keeps its private copy until 424-210 lands.

### 5. Help and documentation

- **`docs/Platform/Storyteller/annotation-lifecycle.md`**: the row of `sform story edit`, and the command in the paragraph on `403` without `Annotation.Manage`.
- **`docs/42for.net/cli/sform.md`**: the scopes `annotation-read`, `annotation-write` and `annotation-manage`.
- **`docs/42for.net/platform/annotations.md`**: the command in the lifecycle examples and in the paragraph on writing a deleted key.
- **`docs/Platform/Storyteller/templating.md`** (not in the plan): how the edit commands read the saved file. This is a change for `config edit` and the others.

### 6. Tests

All 1268 tests of `src/Platform/Cli/test` pass. The build has the same warnings as before the change, and none in the new or changed files.

- **`Model/AnnotationEditDocumentTests`** and **`Model/AnnotationKeyCasingTests`** (the second is not in the plan): every case of the plan. They also cover offsets in the `+hh` form, both dates normalized, values that look like Newtonsoft metadata, and names in mixed case that keep their case.
- **`Commands/StorytellerEditCommandTests`**: every case of the plan, and also:
  - a key deleted while the editor was open;
  - a deleted ancestor listed with the key (both restored, the plural header and hint);
  - both hints after **Restore and edit** below a deleted ancestor;
  - a deleted record purged before its read;
  - a partial restore;
  - a `401` after the editor;
  - a kept edit that cannot be written.

  The mocked editor applies the command's check as `EditorService` does.
- **`Commands/StorytellerEditBodyTests`**: every case of the plan. The deleted unit's lookup is answered without `UnitType`, as the API writes its list. The end-to-end cases run the real `EditorService` with `ScriptedEditor` and the real client: an edit with a comment, a trailing comma, a name in another case and a duplicate label is saved; a change of the form only asks nothing; a broken documentation link re-opens the editor.
- **`Services/EditorServiceTests`**: every case of the plan, and also that the diff shows the check's document and that values which are not JSON re-open the editor.
- **`Commands/StorytellerListCommandTests`**: `edit` is a sub-command with its argument and without `--force`, and the help lists it. The help wraps its lines, so the test compares it with the white space collapsed.

Three mutations of the production code were checked against the tests, and each one made tests fail:
- parsing dates again in the editor's reader;
- taking the deleted record from the lookup list;
- skipping the guard.

### 7. Verification

- `dotnet build` and `dotnet test --project src/Platform/Cli/test`: green, no new warnings.
- Not run against a live API, and not in a terminal: the tool that implemented this has no interactive console, and the command refuses a redirected one. The end-to-end tests stand in for that run.

### 8. Out of scope, and follow-ups

- **The precondition of 424-210**, as the spec describes.
- **`sform story set`**:
  - its private `WithLowerCaseTypeCode` should move to `AnnotationKeyCasing`;
  - its redirected-input check before the deleted-keys question misses a redirected output, which Sharprompt refuses too;
  - its file read (`ToObject` with `JsonSerializer.CreateDefault()`) treats `$ref`, `$id`, `$type` and `$values` as metadata, as the first draft of this command did.
- **The SDK's reads** (`Sdk.NSwag`) use Newtonsoft's default `MetadataPropertyHandling`. A stored value that starts with `$ref` therefore already reads as `null`, and `edit` (like `set`'s merge) writes that `null` back on the next save. `MetadataPropertyHandling.Ignore` in `UpdateJsonSerializerSettings` of the clients would fix it for every reader of the SDK. That is a change of the published SDK, so it was left out here.
- **Numbers** in values are still read as `double`, as documented in the row of the command.
