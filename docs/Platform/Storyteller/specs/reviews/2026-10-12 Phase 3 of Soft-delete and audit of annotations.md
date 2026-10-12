# Phase 3 of Soft-delete and Audit of Annotations

## Overview

Phase 3 covers the CLI (`src/Platform/Cli`) of [Soft-delete and audit of annotations](../2026-10-11%20Soft-delete%20and%20audit%20of%20annotations.md), Linear 424-6. It builds on the API and the regenerated C# SDK of [Phase 2](2026-10-12%20Phase%202%20of%20Soft-delete%20and%20audit%20of%20annotations.md) (`open.api.v0.9.1.json`).

- **Changed commands:** `sform story delete` now soft-deletes, and `--permanent` purges. `get` explains a deleted key. `set` asks before it would re-create a deleted key.
- **New commands:** `restore`, `deleted` (alias `trash`) and `history`.
- **Output:** the history table and its one-line change summaries are in `Output/AuditConsoleExtensions.cs`.

The admin UI is Phase 4.

## What Was Done

### Commands

| Command | File | Notes |
| --- | --- | --- |
| `sform story delete <key> [--permanent] [-y]` | `StorytellerDeleteCommand.cs` | See below |
| `sform story restore <key>... [--without-descendants]` | `StorytellerRestoreCommand.cs` (new) | See below |
| `sform story deleted [-a] [-k]` | `StorytellerDeletedCommand.cs` (new) | See below |
| `sform story history <key> [-n N] [--json]` | `StorytellerHistoryCommand.cs` (new) | See below |
| `sform story get <key>` | `StorytellerGetCommand.cs` | See below |
| `sform story set <key> [--restore-deleted\|--recreate]` | `StorytellerSetCommand.cs` | See below |

#### `delete`

- **Soft delete:**
  - It prints `Deleted '<key>' and N descendants.` and `Restore it with 'sform story restore <key>' before <PurgeAt>; it is purged automatically after that.`
  - A repeated delete prints who deleted the key and when, and exits with `WARNING_NO_WORK_NEEDED`.
- **Permanent delete:**
  1. It finds the key, active or deleted.
  2. Unless `-y` is given, it confirms, with `No` as the default. The prompt names the descendants: the count from `GetDescendants` for an active key, or `Deletion.DescendantCount` for the root of a deletion.
  3. It calls the permanent delete.
- **Deviation 1: no `-p` short name.** `--permanent` has no short form, because `-p` is `--projectKey` on every context command.
- **Deviation 2: the permanent delete is tried before the soft delete.** The spec soft-deletes an active key and then purges it. The CLI instead calls the purge first:
  - If it answers `409 AnnotationNotDeleted`, the CLI deletes the key and purges again.
  - The API checks the role before the state, so a caller without the role gets `403` before anything changes. The message is "Only an Administrator or Owner can delete permanently.", and it exits with `ERROR_WRONG_INPUT`.
  - In the spec's order, such a caller would have left the key soft-deleted.

#### `restore`

- **Requests:** it sends the keys in requests of 100.
- **Output:** it lists the restored keys, and marks the ancestors it had to create as `(created as a missing ancestor)`. It also lists the failures, with the server's message.
- **Exit code:** any failure exits with `ERROR_WRONG_INPUT`.

#### `deleted`

- **Table:** the deleted roots, newest deletion first, with Key, Type, Deleted, By, Descendants and Purged on.
- **`--all`:** lists every deleted annotation, with a Root column.
- **Paging:** the continuation token is handled like the other list commands.

#### `history`

- **Table:** When, Who, Action and Changes, newest first.
- **Notes:** it says when the annotation is deleted, when `--top` hid older entries, and when the audit was trimmed.
- **`--json`:** writes `AnnotationHistory`, limited by `--top` too.
- **Not found:** a purged or unknown key answers "it doesn't exist or was deleted permanently".

#### `get`

On `404`, it asks for the deleted annotation. When the key is deleted, it prints: "The annotation `<key>` is deleted (`<when>` by `<who>`) and will be purged on `<date>`. Restore it with `sform story restore <key>`."

#### `set`

1. **Lookup.** Before writing, it looks up the key and its ancestors (`GetAllAncestorKeys`).
2. **Choice.** When some are deleted, it lists them with who deleted them and when, then picks one of:
   - **Restore and update:** restores the deleted keys with `Descendants = false`, then writes.
   - **Create new:** writes, which re-creates the keys empty.
   - **Cancel:** exits with `WARNING_ABORTED`.

   `--restore-deleted` and `--recreate` answer in advance; both together are rejected. When the input is redirected and neither option is given, it stops with `ERROR_WRONG_INPUT` and names both options.
3. **Errors.** `400` (for example `InvalidAnnotation`) prints the server's message and exits with `ERROR_WRONG_INPUT`. `409` prints the server's message.

#### Shared changes

- **`CommandNames`:** `RESTORE`, `HISTORY`, `DELETED` and `TRASH`.
- **`StorytellerListCommand`:** the three new commands are added to its `[Subcommand]` list.

### Output

`Output/AuditConsoleExtensions.cs`:

- **`WriteHistory`:** writes the history table.
- **`FormatChanges`:** one line per entry, in this order:
  - the cause (`with <key>` for a cascade, `ancestor of <key>`);
  - the descendant count;
  - fields as `Title: "Old" → "New"`, with `Description` and `UnitDefinition` only as `… changed`;
  - labels as `+eu −us`;
  - values as `plan: "pro" → "ent"`, `+key: …` or `−key`, where objects and arrays show `{…}` and `[…]`;
  - the configuration version as `config v7`.

  A cut string ends with `…`.
- **`FormatActor`:**
  - an account prints its name, or else its id;
  - a machine prints `machine:<id>`;
  - the system actor prints `system`.
- **Times and dates:** printed in local time, as `yyyy-MM-dd HH:mm` and `yyyy-MM-dd`.

`Services/AnnotationsApiExtensions.cs` (new) adds:

- `TryGetAnnotationAsync` and `TryGetDeletedAnnotationAsync`, which return null on `404`;
- `GetErrorCode` and `GetErrorMessage` on `ApiException`.

### Tests

`Cli/test`: 106 pass (75 before).

| File | Covers |
| --- | --- |
| `Output/AuditConsoleExtensionsTests.cs` | fields, the description flag, cut strings, labels, values (primitives, kinds, added, removed), cause, descendants, configuration version, actors, action names |
| `Commands/StorytellerDeleteCommandTests.cs` | the soft delete message and a repeated delete. The permanent delete: an active key (purge, `409`, delete, purge), an already deleted key, a declined confirmation, `403` without changes, an unknown key. |
| `Commands/StorytellerSetCommandTests.cs` | nothing deleted; the key deleted with `--restore-deleted`; an ancestor deleted with `--recreate`; both prompt answers; cancel; redirected input without an option; both options; `InvalidAnnotation` |
| `Commands/StorytellerReadCommandsTests.cs` | `get` of a deleted key; `restore` in chunks of 100 with a failure; parsing of many keys; `history` of a purged key |
| `Commands/AnnotationCommandFixture.cs` | the shared doubles: a recording console, a strict `IAnnotationsApiClient`, and the target |

The help of every new and changed command was checked against the built CLI. The commands were not run against a live API.

### Documentation

- **[annotation-lifecycle.md](../../annotation-lifecycle.md):** new section **CLI**.
- **[annotations.md](../../../../42for.net/platform/annotations.md)** (public site): new section **Deleting and restoring**, in product terms, with the commands.
