# Phase 2 of Active Annotations by Default

## Overview

Phase 2 covers section 4 of [Active Annotations by Default](../2026-10-14%20Active%20Annotations%20by%20Default.md), Linear 424-16: the CLI (`src/Platform/Cli`). It builds on the API flags and the regenerated `Sdk.NSwag` of [Phase 1](2026-10-14%20Phase%201%20of%20Active%20Annotations%20by%20Default.md) (`open.api.v0.9.2.json`), where the CLI only passed the new arguments empty.

## What Was Done

### `sform story` (list)

`Commands/Storyteller/StorytellerListCommand.cs`:

- **Options.** `-i|--inactive`, `--deleted` (no short form: `-d` is `--descendants`), and `-a|--all`.
- **Flags.** `InactiveFlag` and `DeletedFlag` are `true` when asked and `null` otherwise, so an empty query string keeps the API default. Every query passes them: the five type queries, the plain list, the descendants, and the subject and responsibility trees with their execution queries.
- **Markers.** `WriteAnnotation` prints `- <key>` and, for an annotation that is not active, a lowlight marker. `StatusSuffix` adds the same marker to the tree labels: ` [subject] [disabled]` on a subject or responsibility node, and ` [responsibility] [expired]` or ` [context] [expired]` on an execution leaf. The time is taken once per run.
- **Continuation token.** `RenderAnnotationsAsync` passes `--continuation-token`; before, it passed `null`.
- **Clean-up in the touched file:** the `using` order (SA1210) and the trailing blank line (SA1518).

`Output/AnnotationConsoleExtensions.cs` (new) has `FormatStatusMarker(SdkAnnotation, DateTimeOffset now)`:

- `[deleted]` when `Deletion` is set, whatever the status;
- otherwise `[disabled]`, `[scheduled]` or `[expired]` from `AnnotationStatusExtensions.GetStatus` (Phase 1), so the CLI and the server share the rule;
- empty for an active annotation.

### `get` and `delete --permanent`

- **`StorytellerGetCommand`.** It asks with `includeInactive: true`, so a disabled, scheduled or expired annotation is printed as JSON with its `IsDisabled`, `ValidFrom` and `ExpiresAt`. A deleted key still answers `404`, and the command keeps explaining it through `GetDeletedAnnotation`. It doesn't use `includeDeleted`, so that message stays as it is.
- **`StorytellerDeleteCommand`.** The permanent delete finds the key through `TryGetAnnotationAsync`. It counts the descendants for the confirmation with `includeInactive: true`, because the delete cascades to inactive descendants too.
- **`TryGetAnnotationAsync`** (`Services/AnnotationsApiExtensions.cs`) always asks with `includeInactive: true`. Its documentation says it returns an annotation that is not deleted, whatever its status.
- **No status note on `get`.** The plan didn't ask for one, and none was added, because a line before the JSON would break scripts that read the output.

### Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md`, section **CLI**: a row for the list with the three options and the markers. The `delete --permanent` row now counts inactive descendants, and the `get` row prints every status.
- `docs/42for.net/platform/annotations.md` (public site): the table row of `isDisabled` and the paragraph after it. They now say that the API answers only active records by default, that the admin UI and `sform story --inactive` ask for the others, and that configuration is not filtered. This text was written in Phase 1 and committed here, because it names the CLI option.

### Tests

`Cli/test`: 116 pass (110 before).

| File | Covers |
| --- | --- |
| `Commands/StorytellerListCommandTests.cs` (new) | the default list asks with empty flags; `--inactive` asks for them and marks `[disabled]`, `[scheduled]`, `[expired]`; `--all` asks for both and marks `[deleted]`; `-k` reaches the plain list; `--subjects` with `--deleted`; the descendants with `--inactive` |
| `Commands/StorytellerDeleteCommandTests.cs` | the strict mocks expect `includeInactive: true` for the key and the descendant count |
| `Commands/StorytellerReadCommandsTests.cs` | `get` asks with `includeInactive: true` |

The help of `sform story` was checked against the built CLI. The commands were not run against a live API.
