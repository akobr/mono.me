# sform story set Merges by Default

Follows [sform story set Sends an Incomplete Annotation Body](2026-11-07%20sform%20story%20set%20Sends%20an%20Incomplete%20Annotation%20Body.md), in the same branch (PR #169). The owner reversed that spec's section 3: the command now merges, as `sform story config set` does.

## Problem

With the body fixed, `sform story set` replaces the content of an existing key, as the API's `PUT` does. `sform story set sbt.northwind -l eu` therefore clears the title, description, documentation link, validity window, time zone, disabled flag, and the other values and labels, and a unit goes back to `event` and `unknown`. **Restore and update** restores a deleted record, then clears it the same way.

`sform story config set` works the other way round. It merges into the stored content by default, and replaces it only with `--replace`. So the two `set` commands of one tool disagree, and the one whose name suggests "set one thing" is the one that wipes the rest. 424-210 describes the same risk on the platform's side.

## Current State

- **`StorytellerSetCommand`** (`src/Platform/Cli/src/Commands/Storyteller/StorytellerSetCommand.cs`) builds the input from `-i`, `-c` and `-l`. `HandleDeletedKeysAsync` looks up deleted keys and may restore them, then `SetAnnotationAsync` writes `annotation.ToSdkAnnotation()`.
- **`ToSdkAnnotation`** (`src/Platform/Cli/src/Model/AnnotationExtensions.cs`) writes the input's members, and for a unit its `UnitType` and `UnitDefinition`, else the defaults of a create.
- **`ConfigSetCommand`** has `--replace`: "Replace the stored content of the configuration instead of merging into it (removes properties which are not specified)." Its merge happens on the server.
- **The API has no merge for annotations.** `POST` and `PUT …/annotations/{key}` replace the content (`AnnotationFactory.Normalize`), so a merge has to read first.
- **`GetAnnotationAsync(…, includeInactive, includeDeleted)`** reads one annotation, and answers `404` for a missing or deleted key. `StorytellerGetCommand` reads with `includeInactive: true`.

## Proposed Changes

### 1. `--replace`

A new option, worded like that of `config set`: "Replace the content of an existing annotation instead of merging into it: what the file and the options do not hold is cleared." With it, the command does what it does now, and reads nothing first.

### 2. The merge

Without `--replace`, after `HandleDeletedKeysAsync`, the command reads the key with `includeInactive: true`. A disabled, scheduled or expired annotation is merged too. A `404` means there is nothing to merge into: the body is the input alone, as for a create.

`ToSdkAnnotation(stored)` takes the stored annotation, and builds the body from it and the input:

| Member | Merged body |
| --- | --- |
| `Title`, `Description`, `DocumentationLink`, `TimeZone`, `ValidFrom`, `ExpiresAt`, `IsDisabled` | The input's when it has one, else the stored one |
| `Labels` | The stored labels, then those of the input that are new |
| `Values` | The stored values, with each value of the input written over the one of the same name. So `-c` wins over the file, and the file over the stored value |
| `UnitType`, `UnitDefinition` | The input's, else the stored one, else the defaults of a create |
| Everything the key decides | From the key, as now |

- **Clearing a member** is what `--replace` is for. In a merge, a member that the file leaves out, or sets to `null`, keeps its stored value, as the file is read into the model and cannot tell them apart.
- **A value is replaced whole.** An object or array value of the file replaces the stored one; no deep merge, as for the other members.
- **The order of the reads** is kept: the lookup of deleted keys, a restore when chosen, then the read. So **Restore and update** merges into the restored record, which is what its label promises: the record keeps its content, with the input written over it. **Create new** reads nothing (`404`), so the re-created key gets the input alone.
- **A race.** Another writer between the read and the write is overwritten for the members the input leaves out. The API offers no precondition on an annotation write (424-210), so this stays.

### 3. Failures

A read that fails with anything but `404` is handled as a failed write is: a `403` prints the server's message (`WriteApiError`), and another status prints the error. The write is not sent.

### 4. Help and documentation

- **The command's description:** "Create an annotation, or merge into an existing one; --replace clears what the file and the options do not hold."
- **The help of `-c` and `-l`:** unchanged; their wording still holds.
- **`docs/Platform/Storyteller/annotation-lifecycle.md`:** the row of `sform story set`, with the merge, `--replace` and its options.
- **`docs/42for.net/platform/annotations.md`:** **Restore and update** now keeps the record's content.

### 5. Tests

- **`StorytellerSetBodyTests`**, through the real client:
  - a merge over a stored annotation, member by member as in the table;
  - a merge over a stored unit, which keeps its type and definition;
  - `--replace`, which reads nothing and sends the input alone;
  - `404`, which sends the input alone;
  - a failed read, which sends nothing.
- **`StorytellerSetCommandTests`** (mocked client): the read is set up, and the order of calls is checked: lookup, restore, read, write.

### 6. Verification

The test suites of `sform`, and the built `sform` against a throwaway fake of the API that answers a stored annotation. Not against a live API, as for the first fix.
