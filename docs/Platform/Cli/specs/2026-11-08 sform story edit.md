# sform story edit

Linear 424-217. A new command, `sform story edit <annotation-key>`, opens an annotation in the configured editor and saves it when you confirm, as `sform story config edit` does for a configuration.

## Problem

`sform` can create and change an annotation only through `sform story set`. That command takes the content from a file (`-i`) and from options (`-c name=value`, `-l label`). To change the title of an annotation or remove one of its labels, you must export it with `sform story get -e`, edit the file, and send it back with `sform story set -i <file> --replace`. Configurations, templates and schemas each have an `edit` command that does these steps in one go. Annotations have none.

## Current State

- **`ConfigEditCommand`** (`src/Platform/Cli/src/Commands/Configuration/ConfigEditCommand.cs`) is the model. It reads the stored content (lines 57-61). When there is none, it prints "Configuration for '<key>' does not exist yet, creating new." (65). It edits the content through `IEditorService.EditJsonAsync` with `EditorOptions` (68-73), and replaces the stored content through a JSON Patch of the diff (82-90). It returns the editor's exit code when there is nothing to save. A change it does not detect prints "No changes detected." and returns `WARNING_NO_WORK_NEEDED` (92-96). A save returns `SUCCESS`. A schema violation returns `ERROR_WRONG_INPUT`, and any other API error `ERROR_CRASH` (101-110). `TemplateEditCommand` does the same and prints the server's message for a `403` (`WriteApiError`).
- **`EditorService.EditJsonAsync(console, options, original, fileNamePrefix, isNew, confirm = true)`** (`src/Platform/Cli/src/Services/EditorService.cs:135-242`):
  1. It sets up the editor on first use (a `Select`).
  2. It writes the document to a temporary file, named from the prefix with `Path.GetInvalidFileNameChars` replaced, and opens the editor with the quoted path.
  3. When the editor exits with a non-zero code, it returns `ERROR_CRASH`.
  4. It parses the file with `JObject.Parse`. On invalid JSON, it asks to re-open the editor, and returns `WARNING_ABORTED` when you decline.
  5. When the document is deep-equal to the original, it prints "No changes detected." and returns `WARNING_NO_WORK_NEEDED`.
  6. It shows the diff, asks "Do you want to save these changes" (returns `WARNING_ABORTED` on no), and returns the edited document. The temporary file is deleted in a `finally`.
  - **`JObject.Parse` turns date-like strings into dates** (`DateParseHandling.DateTime`). The SDK reads every document with `DateParseHandling.None` (`Sdk.NSwag/src/ApiSdk.partial.cs`), so the original document holds strings. A deep comparison of a string with a date is false, so a document with a date-like string never compares equal to its original, even when nothing was changed. The edited value is also written back in this machine's offset. This already affects `config edit`, `config patch`, `template edit` and `schema … edit`. It would break the "no changes" outcome of an annotation with `ValidFrom` or `ExpiresAt`.
  - `JObject.Parse` keeps the last of two properties with the same name (`DuplicatePropertyNameHandling.Replace`), and reads at most 64 levels, while the configurations client reads 256 (`MaxDepth`, `ApiSdk.partial.cs`).
  - Only invalid JSON re-opens the editor, and the diff and the question are about the parsed file. A command that checks or changes the document after the edit can only fail or save something you did not confirm. Your changes are then lost with the temporary file.
  - Sharprompt prompts need an interactive console. With redirected input, the first `Confirm` or `Select` throws.
- **`StorytellerSetCommand`** (`src/Platform/Cli/src/Commands/Storyteller/StorytellerSetCommand.cs`) writes with `SetAnnotationAsync(…, annotation.ToSdkAnnotation(stored))`. It looks up the deleted keys first (`HandleDeletedKeysAsync`): the key and its ancestors. When some are deleted, it lists them as `- <key> (<time> by <actor>)` (`AuditConsoleExtensions.FormatDeletion`) and asks: **Restore and update**, **Create new** or **Cancel**. It restores with `Descendants = false`. When the input is redirected, it stops with `ERROR_WRONG_INPUT`. It writes the key with its type code in lower case (`WithLowerCaseTypeCode`, private). It handles API errors this way:
  - a `400` or `409` prints the server's message (`GetErrorMessage`), with `ERROR_WRONG_INPUT` for a `400` and `ERROR_CRASH` for a `409`;
  - a `403` prints the server's message and code (`WriteApiError`, `WARNING_UNAUTHORIZED_ACCESS`);
  - a `JsonException` (the client refused to serialize the body) prints "The annotation <key> could not be sent: …" and returns `ERROR_CRASH`.
- **`sform`'s top level** (`Program.cs`) prints a `401` or `403` that a command does not catch as a refusal and exits with `WARNING_UNAUTHORIZED_ACCESS`. Any other exception, a network error for example, is a crash.
- **`ToSdkAnnotation()`** (`src/Platform/Cli/src/Model/AnnotationExtensions.cs`) without a stored annotation builds the body from the input alone. It adds the members the key decides (`CreateForKey`), removes duplicate labels, and leaves the relation arrays out. A unit without `UnitType` or `UnitDefinition` gets `event` and `unknown`.
- **The reads.** `AnnotationsApiExtensions.TryGetAnnotationAsync` reads a key with `includeInactive: true`. It returns `null` on `404`, which also covers a deleted key. `TryGetDeletedAnnotationAsync` reads one deleted record. **`FindDeletedAnnotationsAsync` answers the base members only:** the API writes the list with System.Text.Json by its declared element type `Annotation` (`AnnotationsHttp.Deleted.cs:117-118`). A deleted unit in that list therefore has no `UnitType` or `UnitDefinition`, while the read of one deleted record (`OkObjectResult(annotation)`, by its runtime type) has them.
- **The rules of the content** (`Backend.Core/src/Annotating/AnnotationRules.cs`): a title of at most 200 characters, a description of at most 10000, and a documentation link of at most 2048 that is an absolute http or https URL without white space. The limits are `AnnotationLimits` in Abstractions.Annotations, which `sform` references. Stored annotations are not migrated, so a legacy annotation can break a rule until its next write.
- **The write replaces the content** (`POST`/`PUT …/annotations/{key}`). It has no precondition: 424-210 plans `ETag`/`If-Match` and a merge `PATCH`. A replace with no precondition loses every concurrent change, not only those of the members you edited. `config edit` sends a JSON Patch of the diff, so a concurrent change of another property survives there.
- **Numbers.** The annotations client reads values with Newtonsoft's default `FloatParseHandling.Double`, so a number with more than about 15 significant digits, or with trailing zeros (`19.90`), is read rounded. `set`'s merge already sends the stored values back that way.
- **The file of an annotation in an export** (`Backend.Core/src/Transferring/ViewAnnotationDocument.cs`) already defines the editable members, in the API's PascalCase names and in this order: `Title`, `Description`, `DocumentationLink`, `Labels`, `Values`, `IsDisabled`, `ValidFrom`, `ExpiresAt`, `TimeZone`, and for units `UnitType` and `UnitDefinition`. Names match in any case. Its non-editable members are the other properties of the core runtime type. Since `sform` does not reference Backend.Core, it cannot reuse that code.
- **Registration:** `StorytellerListCommand` lists its sub-commands in `[Subcommand(…)]` (lines 18-30). `CommandNames.EDIT` exists. Commands are built through constructor injection, and `IEditorService`, `EditorOptions` and `IFileSystem` are registered already.
- **Docs:** the CLI table in `docs/Platform/Storyteller/annotation-lifecycle.md` ("CLI", one row per `sform story` command), the scopes table in `docs/42for.net/cli/sform.md`, and "Deleting and restoring" in `docs/42for.net/platform/annotations.md`.
- The Linear issue has no CodeRabbit comments and no coding plan. Three independent reviews of a draft of this spec (code facts, user experience, edge cases) shaped sections 1 to 3.

## Proposed Changes

### 1. `StorytellerEditCommand`

A new file, `src/Platform/Cli/src/Commands/Storyteller/StorytellerEditCommand.cs`:

```csharp
[Command(CommandNames.EDIT, Description = "Edit the content of an annotation in your preferred editor. The saved document replaces the content.")]
public class StorytellerEditCommand : BaseContextCommand
{
    public StorytellerEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions,
        IFileSystem fileSystem);

    [Argument(0, Description = "The annotation key to edit.")]
    public string AnnotationKey { get; set; } = string.Empty;
}
```

It has no `-f|--force`, because annotations have no schema. It is registered in `StorytellerListCommand`'s `[Subcommand(…)]` after `StorytellerSetCommand`, so `sform story --help` lists it.

**The flow:**

1. **The key.** `Console.ValidateAnnotationKey`, then its type code in lower case (section 4). Every request uses that key.
2. **An interactive console.** The editor and its questions need one. With redirected input, the command prints "sform story edit needs an interactive console. Without one: sform story get <key> -e <file>, edit the file, then sform story set <key> -i <file> --replace." and returns `WARNING_INTERACTION_NEEDED`, before any request.
3. **The read.** `TryGetAnnotationAsync` (`includeInactive: true`), so you can edit a disabled, scheduled or expired annotation too.
4. **A missing key.** When the read returns nothing, the command looks up the key and its ancestors with `FindDeletedAnnotationsAsync`, as `set` does and as "Creating in clients" in `annotation-lifecycle.md` asks. Saving would otherwise re-create a deleted key empty, without its configuration and history.
   - **Nothing deleted:** it prints "Annotation '<key>' does not exist yet, creating new." and edits an empty document (section 2) as new.
   - **Some deleted:** it lists them as `set` does (`- <key> (<time> by <actor>)`) and asks:
     - **Restore and edit (keeps the configuration and the history)**, the default. If the key itself is among the deleted, you edit its deleted record, read with `TryGetDeletedAnnotationAsync` (the lookup's list lacks the members of the subtypes, such as a unit's type and definition). Otherwise, or when that read finds nothing, you edit an empty document as new. The listed keys are restored only when you save (step 7).
     - **Create new (starts with an empty configuration)**: you edit an empty document as new, and the write re-creates the deleted keys empty, as `set`'s **Create new** does.
     - **Cancel**: it prints "Nothing was changed." and returns `WARNING_ABORTED`. The editor does not open.
5. **The edit.** `EditJsonAsync(Console, options, document, $"annotation-{key}", isNew, check: …)` (section 3), where the check is `AnnotationEditDocument.Check` for the key (section 2). A problem re-opens the editor on your text. The service compares, shows and returns the normalized document, so the diff you confirm is the content that is written.
6. **Nothing to save.** When the editor returns no document, the command returns the editor's exit code:
   - `WARNING_NO_WORK_NEEDED` when nothing changed, a change of the form only included (`"Labels": null` for `[]`, another case of a name). For a new key, it also prints "Nothing was created. To create it without content: sform story set <key>". After **Restore and edit**, it prints "Nothing was restored. To restore it as it was: sform story restore <keys> --without-descendants".
   - `WARNING_ABORTED` when you decline.
   - `ERROR_CRASH` when the editor fails.
7. **The save.**
   1. **The guard.** It reads the key again (`TryGetAnnotationAsync`). If the annotation changed since step 3, it does not save. That means the editable document of the new read differs from the one you started from, or an annotation now exists where there was none. It prints "The annotation '<key>' changed while you edited it, so your edit was not saved.", keeps your edit (step 7.4), and returns `ERROR_WRONG_INPUT`, as `sform` does for a `412`. Until 424-210, this narrows the window in which a concurrent change is lost from the time in the editor to that of one request.
   2. **The restore.** After **Restore and edit**, it restores the listed keys with `RestoreAnnotationsAsync` (`Descendants = false`, as in `set`) and prints "Restored <keys>." If a key fails, it prints each failure, keeps your edit, and returns `ERROR_WRONG_INPUT`. It writes nothing.
   3. **The write.** `SetAnnotationAsync(…, key, AnnotationEditDocument.ToAnnotation(…).ToSdkAnnotation())`, the body of the edited document alone. The write replaces the content, so a member, label or value you removed is removed on the server too. On success it prints "Annotation '<key>' has been saved." and returns `SUCCESS`.
   4. **Keeping the edit.** When anything fails after the editor returned a document, the guard included, the command writes the normalized document to a new file in the temporary folder (`IFileSystem`, `annotation-<key>-<random>.json`, the name as in section 3). It prints "Your edit is in <path>. To save it anyway: sform story set <key> -i <path> --replace". `set` reads the same members and replaces the content with them. When the restore succeeded and the write failed, it also prints "The restored annotations keep the content they had when they were deleted." An exception the command does not handle is re-thrown after that, so the top level reports it as before.

**Errors**, for the read, the lookup, the restore and the write:

| Failure | Output | Exit code |
| --- | --- | --- |
| `400` (for example `InvalidAnnotation`, which names the field and its limit) | the server's message | `ERROR_WRONG_INPUT` |
| `409` | the server's message | `ERROR_CRASH` |
| `403` (for example a machine without `Annotation.Write`, or without `Annotation.Manage` for the restore) | `WriteApiError`: the server's message and code | `WARNING_UNAUTHORIZED_ACCESS` |
| Another status, except `401` | "Error occurred: …" | `ERROR_CRASH` |
| `401`, a network error, another exception | not caught: `sform`'s top level reports it, as for every command (a `401` as a refusal with `WARNING_UNAUTHORIZED_ACCESS`, anything else as a crash) | |
| `JsonException` (the client refused to serialize the body) | "The annotation <key> could not be sent: …" | `ERROR_CRASH` |

After the editor, each of them also keeps your edit (step 7.4).

### 2. `AnnotationEditDocument`

A new file, `src/Platform/Cli/src/Model/AnnotationEditDocument.cs`, a static class. It builds the document you edit, checks and normalizes it, and turns it back into an annotation. Its members and their order are those of the export file (`ViewAnnotationDocument`). Unlike the export file, the document **lists every member, `null` when unset, `[]` and `{}` for no labels and no values**, so you can see what you can fill in.

```json
{
  "Title": "Invoicing",
  "Description": null,
  "DocumentationLink": null,
  "Labels": ["eu"],
  "Values": { "tier": "gold" },
  "IsDisabled": null,
  "ValidFrom": "2026-12-01T00:00:00+01:00",
  "ExpiresAt": null,
  "TimeZone": "Europe/Prague"
}
```

A unit also has `"UnitType"` and `"UnitDefinition"`, after `TimeZone`. A new unit gets `"event"` and `"unknown"`.

- **`JObject Create(AnnotationKey key, Sdk.Annotation? annotation)`** builds the document from the SDK model, or the empty document without one.
  - Dates are strings in Newtonsoft's ISO format (`yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz`), so they compare as strings with the parsed file.
  - Values are copied as the SDK read them, so date-like strings stay as stored. A `null` value stays a JSON `null`.
  - For a unit, `UnitType` and `UnitDefinition` come from `Sdk.Unit`.
- **`string? Validate(AnnotationKey key, JObject document)`** returns the first problem, or `null`:
  - **The names.** A member name matches in any case. A member twice in another case is an error ("The member 'Title' is in the document twice."). Twice in the same case is invalid JSON already (section 3). `UnitType` and `UnitDefinition` are members of units only ("'UnitType' is a member of units only.").
  - **A member that is not editable**, one of the other properties of the key's core runtime type (`AnnotationTypes.GetRuntimeType`), as in the export file: the key, the type, the names, the keys of the relations, the relation arrays, the stamps and the deletion. "'Name' cannot be edited: the key decides the type, the names and the relations, and the server keeps the stamps." The export file ignores them, so that a saved read can be imported. A document you edit never has them, so one you add was meant to change something, and ignoring it would hide that it did nothing.
  - **Any other member:** "Unknown member 'Titel'. The members are Title, Description, …".
  - **The types:**
    - `Title`, `Description`, `DocumentationLink`, `TimeZone`, `UnitType`, `UnitDefinition`: a string or `null`.
    - `IsDisabled`: `true`, `false` or `null`.
    - `ValidFrom`, `ExpiresAt`: an ISO 8601 date and time as the API reads it (System.Text.Json's `TryGetDateTimeOffset`) **with an explicit offset or `Z`**, or `null`. A value without an offset would otherwise be taken in this machine's offset, while the annotation's `TimeZone` is meant for that: "'ValidFrom' must be a date and time with an offset, for example "2026-12-01T00:00:00+01:00", or null."
    - `Labels`: an array of strings, or `null`.
    - `Values`: an object, or `null`.
  - **The rules of `AnnotationRules`**, with the server's wording and the limits of `AnnotationLimits`: the lengths of `Title`, `Description` and `DocumentationLink`, and a `DocumentationLink` that is an absolute http or https URL without white space. They come up while the editor can still be re-opened, also for a legacy annotation that breaks them. The server still checks everything, and the command prints its message (section 1).
- **`JObject Normalize(AnnotationKey key, JObject document)`** turns a valid document into the form `Create` writes:
  - every member in its canonical name and order, a missing member `null`;
  - `Labels` without duplicates (ordinal, the first occurrence kept), `[]` for `null`;
  - `Values` `{}` for `null`;
  - the dates written as `Create` writes them;
  - for a unit, `event` or `unknown` for a missing or `null` `UnitType` or `UnitDefinition`, as `set --replace` and an import do. The diff shows it.
- **`JsonEditCheck Check(AnnotationKey key, JObject document)`**: the problem of `Validate`, or the document of `Normalize`. This is the check that the command hands to the editor service.
- **`CoreAnnotation ToAnnotation(AnnotationKey key, FullKey fullKey, JObject normalized)`**: the core model of the key's runtime type. It is read with the default serializer, as `set` reads a file, so date-like strings in the values stay strings. It then gets the key, type, name, project and view, as `set` sets them. `ToSdkAnnotation()` then adds the members the key decides.

### 3. `EditorService`

- **A check that normalizes.** `IEditorService.EditJsonAsync(console, options, original, fileNamePrefix, isNew, confirm = true, check = null)` gets a last optional parameter, `Func<JObject, JsonEditCheck>? check`. `JsonEditCheck` (`src/Platform/Cli/src/Services/JsonEditCheck.cs`) is a record of the document or the problem, with `Valid(JObject)` and `Invalid(string)`.
  - After a successful parse, the service calls the check.
  - A problem is printed (`WriteImportant`) and treated like invalid JSON. "Would you like to re-open the editor to fix the issue" re-opens the editor on the same file, so your text stays. A no returns `WARNING_ABORTED`.
  - A valid document **replaces the parsed one** for the comparison with the original, the diff, the question and the result. A change of the form only is then "No changes detected." before any question, and the diff shows what the caller will save.
  - The other callers pass nothing and behave as before.
- **The reader of the edited file** is a `JsonTextReader` with `DateParseHandling.None` and `MaxDepth = 256`, read by `JObject.Load` with `DuplicatePropertyNameHandling.Error` and comments ignored, then `while (reader.Read()) { }`, so content after the object is still an error and a comment after it is not.
  - A date-like string stays the string you wrote, as the SDK reads documents. This also fixes `config edit`, `config patch`, `template edit` and `schema … edit`: a configuration with a date-like string no longer looks changed, and its value is no longer rewritten in this machine's offset.
  - A property twice in one object is invalid JSON and re-opens the editor, for every caller, so the same file no longer means its last property silently.
  - A configuration deeper than 64 levels, which the API stores up to 128, can be edited again.
- **The temporary file name** keeps the letters, digits, `.`, `-` and `_` of the prefix and replaces any other character with `_`. An annotation name may hold `"` or `%`, which would break the quoted path of the editor's command line (`cmd.exe` expands `%…%` inside quotes). This applies to every caller.
- **`OpenFileInEditorAsync` becomes `virtual`**, so a test can stand in for the editor and drive the loop.
- The Moq setups of `EditJsonAsync` in `TemplateCommandsTests` and `SchemaOperationsTests` keep compiling. Moq matches the omitted `check` as `null`, which is what those callers pass.

### 4. The key in lower case

`AnnotationKeyCasing.WithLowerCaseTypeCode(this AnnotationKey key)`, a new file `src/Platform/Cli/src/Model/AnnotationKeyCasing.cs`, does what `StorytellerSetCommand`'s private method does. The API stores an annotation under its key as written, so a type code in another case would be a second annotation next to the real one. `StorytellerSetCommand` keeps its private copy for now. 424-210 rewrites the code right next to it in a parallel branch, and moving it there would conflict. A follow-up switches `set` to the shared method.

### 5. Help and documentation

- **The help** of the command and its argument as in section 1. `sform story --help` lists `edit`.
- **`docs/Platform/Storyteller/annotation-lifecycle.md`**, the CLI table: a row for `sform story edit <key>` with the document, the checks, the deleted keys, the replace, the guard, the outcomes and their exit codes, the kept edit, the interactive console, and the rounding of long numbers.
- **`docs/42for.net/cli/sform.md`**, the scopes table:
  - `annotation-read`: `edit` reads the annotation and looks up the deleted keys;
  - `annotation-write`: `sform story set` and `edit`;
  - `annotation-manage`: the restore of `sform story edit`.
- **`docs/42for.net/platform/annotations.md`**: `sform story edit` in the lifecycle examples, and in the paragraph on writing a deleted key, next to `sform story set`.

### 6. Tests

In `src/Platform/Cli/test`:

- **`Model/AnnotationEditDocumentTests`** (new):
  - `Create` for each type with no annotation: every member `null`, `[]` or `{}`, and the unit members only for a unit, with `event` and `unknown`;
  - `Create` from a stored annotation and a stored unit: the members, the dates as strings, date-like values unchanged, a `null` value, nested objects and arrays, a big integer;
  - `Validate`:
    - every type rule, including non-string labels;
    - a date without an offset, a date in another notation (`01.12.2026`);
    - a member twice in another case, a unit member on another type;
    - a non-editable member (`AnnotationType`, `Name`, a key of a relation, a stamp) and an unknown member;
    - the lengths and the URL rule at their limits and beyond;
    - a valid document in other casing;
  - `Normalize`: missing members, `null` labels and values, duplicate labels, a date in another format, the unit defaults, and that `Create` then `Normalize` is the same document;
  - `ToAnnotation`: the runtime type, the identity, the values, and the unit members.
- **`Commands/StorytellerEditCommandTests`** (new; mocked API in `MockBehavior.Strict` and a mocked editor; the editor's document, `isNew` and the check are checked):
  - an existing key: it is read with `includeInactive: true`, edited, guarded and saved as a replace (a removed title, label and value are not in the body), and returns `SUCCESS` with the saved message;
  - a missing key, with no deleted keys: it prints the "does not exist yet" line, edits an empty document as new, and saves;
  - the editor's outcomes: nothing changed returns `42` and writes nothing, with the hint for a new key; declined returns `40`; a failed editor returns `1`;
  - the check handed to the editor rejects an invalid document and normalizes a valid one;
  - redirected input returns `43` before any request;
  - deleted keys:
    - **Cancel** returns `40` and does not open the editor;
    - **Create new** edits an empty document as new and writes without a restore;
    - **Restore and edit** of the key edits its deleted record, as read on its own, then guards, restores and writes, in that order;
    - **Restore and edit** of a deleted ancestor edits an empty document and restores the ancestor;
    - **Restore and edit** with an unchanged document restores nothing and prints the hint with `--without-descendants`;
    - a failed restore returns `2`, writes nothing, and keeps the edit;
  - the guard: a change while the editor was open, and a key created meanwhile, return `2`, write nothing, and keep the edit;
  - errors:
    - on the write, a `400` (`2`, the server's message), a `409` (`1`), a `403` (`44`) and a `500` (`1`, "Error occurred"), each keeping the edit;
    - a restore that succeeds followed by a write that fails, with the line about the restored content;
    - a `403` on the read and a `403` or `500` on the lookup, which open no editor;
    - a `401`, re-thrown, which keeps the edit when it comes after the editor;
  - a type code in another case is read and written as lower case.
- **`Commands/StorytellerEditBodyTests`** (new, through the real generated client and `ScriptedHttpHandler`, as `StorytellerSetBodyTests`). They check what is read and sent, which a mocked client never does:
  - the body of every type is what the API reads (`AnnotationsHttp`'s options);
  - a removed member, label and value are not in the body;
  - the relation arrays and the stamps stay out, the members the key decides are in;
  - date-like strings of the stored values, and `ValidFrom`, reach the body as stored;
  - a unit keeps its type and definition, and gets the defaults without them;
  - the requests: the read, the guard and the write to the route of the key; for a new key, the lookup before the editor;
  - **Restore and edit** of a deleted unit, with the lookup answered as the API writes it (no `UnitType`): the document and the body keep the unit's type and definition from the read of the deleted record;
  - a body that the client cannot write ends with the "could not be sent" line, `1`, and the kept edit;
  - end to end, with the real `EditorService` and a stand-in editor: an edit is saved, and a change of the form only saves nothing.
- **`Services/EditorServiceTests`** (new; `MockFileSystem` and a subclass whose `OpenFileInEditorAsync` writes the "edited" file):
  - an unchanged document with date-like strings returns `42`;
  - an edited date-like string stays a string;
  - a document the check rejects re-opens the editor on your text and is then accepted;
  - a rejected document returns `40` when you decline;
  - the document of the check is the one compared, shown and returned, so a change of the form only returns `42` without a question;
  - invalid JSON, content after the object, and a property twice re-open the editor; comments inside and after the object are accepted; a document of 100 levels is read;
  - the temporary file name has no `"` or `%`, and the file is deleted.
- **`Commands/StorytellerListCommandTests`**: `sform story`'s sub-commands include `edit`, with its argument and without `--force`, and the help text lists it.

### 7. Verification

- `dotnet build` of `src/Platform/Cli` with no new warnings (StyleCop).
- `dotnet test --project src/Platform/Cli/test`.
- No run against a live API, and no run in a terminal (the tool that implements this has no interactive console). The end-to-end tests of section 6 run the command with the real editor service, the real client and a stand-in editor.

### 8. Out of scope

- **The precondition.** Once 424-210 lands, the guard (step 7.1) gives way to its `If-Match`. The read and the write sit in small private methods, so the follow-up swaps them for the versioned SDK calls and passes the tag through:
  - an existing key: a versioned read, then the write with `If-Match: <tag>`;
  - a new key, or **Create new**: the write with `If-None-Match: *`;
  - **Restore and edit**: a deleted record has no tag. So the restore comes first, then a versioned read, a comparison with the deleted record you edited, and the write with `If-Match`.
- **`sform story set`'s private `WithLowerCaseTypeCode`** switches to the shared method after 424-210 (section 4).
- **A merge.** `edit` always replaces: the document holds the whole editable content.
- **Numbers.** Keeping the text of every number from the read to the write needs a read that does not go through `double`, for example a `FloatParseHandling` the whole annotations client would have to take on, while `decimal` cannot read every number the API stores. Until then, the docs say that a long number is saved rounded.
