# Phase B of Versioning of Configuration Templates

## Overview

Phase B of [2026-09-28 Versioning of Configuration Templates](../2026-09-28%20Versioning%20of%20Configuration%20Templates.md) adds template management to the `sform` CLI: get, set, edit, delete, versions, and diff. It builds on the regenerated NSwag SDK. It also covers three changes requested with this phase:

- A `config versions` command, for parity with templates.
- A fix of the merge limitation of `set` and `edit`, which the spec listed as out of scope, for both configurations and templates.
- The CLI build fix for the renamed SDK clients.

The Phase A review is [2026-09-29 Phase A of Versioning of Configuration Templates](2026-09-29%20Phase%20A%20of%20Versioning%20of%20Configuration%20Templates.md).

The CLI builds with no new warnings. The new `Cli.UnitTests` project passes 8 of 8 tests. The commands were smoke-tested through `--help`, but were not run against a live API (see *Verification*).

## What Was Done

### 10. SDK regeneration (`Storyteller/Sdk.NSwag`)

- The user regenerated `ApiSdk.g.cs` from the new `open.api.v0.8.81.json`, after renaming the OpenAPI tags. The clients are now `IConfigurationsApiClient`, `ISchemasApiClient`, and `ITemplatesApiClient`, and the user already registered them in `ServicesCollectionExtensions` and `ApiSdk.partial.cs`.
- Deviation: the spec expected `ITemplateApiClient` (tag `Template`). The generated names follow the renamed tags.
- `Sdk.NSwag/README.md`: the generation command now points to `open.api.v0.8.81.json`.
- **CLI build fix.** The only break was the rename `IConfigurationApiClient` → `IConfigurationsApiClient` in the five `Commands/Configuration/*` commands. No other project in `src/` used the old names.

### 11. CLI commands (`src/Platform/Cli/src/Commands/Templates/`)

Registered under `sform storyteller` (alias `story`) through `StorytellerListCommand`:

| Class | Command |
| --- | --- |
| `TemplateGetCommand` (parent) | `template|templates <type> [--version <n>] [-e|--export <file>]` |
| `TemplateSetCommand` | `set|create <type> [-i] [-x ...] [--replace]` |
| `TemplateEditCommand` | `edit <type>` |
| `TemplateDeleteCommand` | `delete|remove <type>` |
| `TemplateVersionsCommand` | `versions <type>` |
| `TemplateDiffCommand` | `diff <type> [to] [from]` |

- Organization, project, and view come from `ICommandContext`, and `-p|--projectKey` / `-v|--view` override them, as for every `BaseContextCommand`. Templates are per view (decision D2).
- New `ExtendedConsoleExtensions.ValidateAnnotationType` checks the type code against `AnnotationTypeCodes.ValidCodes` and lower-cases it. An unknown code prints the known codes and ends with `ERROR_WRONG_INPUT` before any request is sent.
- 404 responses print a "has not been found" message and return `ERROR_WRONG_INPUT`, as the config commands do.
- `CommandNames` has three new names: `TEMPLATE`, `TEMPLATES`, and `VERSIONS`.
- Deviation: `set --replace` is new (section 14). `edit` replaces the content instead of merging (section 14).

### 12. CLI refactoring to avoid duplication

- `Output/DiffResultConsoleExtensions.WriteDiffResult`: the hunk rendering and `RenderLineWithSegments`, moved from `ConfigDiffCommand` without changes, including the "No changes detected." message. `ConfigDiffCommand` and `TemplateDiffCommand` use it.
- `Commands/JsonInputBuilder.BuildAsync`: the `--import` + `--properties` document builder, moved from `ConfigSetCommand` with the same merge settings and messages. Wrong input now ends with `WrongInputException`, which returns `ERROR_WRONG_INPUT` as before.
- `IEditorService.EditJsonAsync(console, options, original, fileNamePrefix, isNew)` returns a `JsonEditResult(Edited, ExitCode)`. It covers the editor round-trip moved from `ConfigEditCommand`: editor setup on first use, temp file, re-edit on invalid JSON, change detection, diff, confirmation, and cleanup. The exit codes are unchanged.
  - Deviation: changes are now detected by `JToken.DeepEquals` instead of comparing re-serialized text, so a change in property order alone no longer counts as a change.
  - The temp file name now uses `IFileSystem.Path` (analyzer IO0006).
- `Output/VersionsConsoleExtensions.WriteVersions` renders the version table (newest first; `—` as expiration of the current version). `Model/ConfigurationVersionExtensions.IsCurrent()` recognises the current version by its `DateTimeOffset.MaxValue` expiration.
- `IEditorService`: the existing members were re-indented. The file had inconsistent indentation (SA1137).

### 13. Documentation

- `docs/Platform/Storyteller/templating.md` has a new *CLI* section. It covers the command tree, the behaviour of each command, examples, the configuration counterparts, and the JSON Pointer limitation (section 14).

### 14. Additional changes requested with Phase B

#### `config versions` and `config get --version`

- New `ConfigVersionsCommand` (`sform story config versions <key>`), registered under `ConfigGetCommand`. It uses the same table as `template versions`.
- `ConfigGetCommand` has a new `--version <n>`, which prints the stored content of a version (`GetConfigurationVersion`). Listing versions is not much use without a way to look at one, and templates have the same option. `--version` together with `--resolved` is rejected.

#### Replace instead of merge for `edit` and `set --replace`

Problem: `SetConfiguration` and `SetTemplate` merge the request into the stored content (`MergeInto`: arrays are unioned, nulls are ignored). A property deleted in the editor stayed on the server, and array items could not be removed.

Fix, on the client side, with no API change:

- New `Json/JsonPatchBuilder.Create(original, target)` builds RFC 6902 operations. Objects are compared property by property: missing properties are removed, new ones added, and changed values replaced. Arrays and scalars are replaced as a whole, so neither the server's array union nor its null handling applies.
- New `Services/StorytellerApiExtensions`:
  - `GetStoredConfigurationContentAsync` and `GetTemplateContentAsync` read the current stored content.
  - `ReplaceConfigurationAsync` and `ReplaceTemplateAsync` save a document. A missing item is created with `Set…`. An existing one is changed with `Patch…` and the computed patch. An empty patch returns `null` ("No changes detected.", `WARNING_NO_WORK_NEEDED`).
- `config edit` and `template edit` always replace.
- `config set` and `template set` keep merge as the default, because inline `-x` properties are meant to add to the document. `--replace` switches them to replacement.

**Fix of `config edit` editing the calculated document.** `config edit` used to load `GetConfiguration`, which returns the *calculated* configuration: ancestors, template, and stored content merged. It then saved the whole document as the configuration's own content, copying inherited and template values into it. A JSON Patch computed from that document would also not match the stored content on the server.

`config edit` and `config set --replace` now read the stored content instead:

- They take the current entry of `GetConfigurationVersions` (the one with the unlimited expiration) and read its content with `GetConfigurationVersion`.
- A configuration that only has history (a deleted one) counts as missing, and is created again.
- A system-created item (empty content, version 0) is edited as `{}` and patched.

**Server limitation found by the tests.** The server applies JSON Patch with JsonPatch.Net 5.0.2 (`JsonExtensions.ApplyPatch`), and the tests showed it does not decode escaped JSON Pointers:

- `remove /c~0d` silently did nothing.
- `replace /a~1b` added a new property literally named `a~1b`.

To stay correct, `JsonPatchBuilder` emits a single `replace` of the root path `""` with the whole target document whenever a changed property name needs escaping (`~` or `/`). The server library handles that correctly. Changes to other names still produce fine-grained operations. This affects any client that sends a JSON Patch with such names; the server-side fix is left for a separate change.

### Tests

New project `src/Platform/Cli/test/Cli.UnitTests.csproj` (xUnit + Shouldly, added to `42.mono.slnx`), with `JsonPatchBuilderTests`. Each test applies the generated patch with the same library and JSON conversion as the server, and checks that the result equals the target document:

- equal documents → no operations;
- removed properties, top level and nested;
- a changed array replaced as a whole (a merge would keep the removed item);
- added, replaced, and `null` values (a merge would ignore the `null`);
- changed value types (object ↔ scalar, array ↔ object);
- a changed name with `~` or `/` → a single root `replace`;
- an unchanged name with `/` next to a changed plain property → a fine-grained patch;
- an empty target removes everything.

Result: **8 of 8 pass**.

### Verification

- `dotnet build src/Platform/Cli/src` succeeds, with no warnings from the new or changed files. The remaining CLI warnings (SA1518, SA1210, CS8618, …) were already there.
- `sform story template --help`, `story template set --help`, and `story config versions --help` show the expected command tree and options.
- The commands were **not** run against a live API. The locally running `Api.Functions` may not have the current code, and the commands need a signed-in account. What still needs a live check:
  - the new `IsCurrent()` detection with real `ConfigurationVersion` payloads;
  - the PATCH round-trip through the regenerated SDK (`object` body serialized by Newtonsoft).
