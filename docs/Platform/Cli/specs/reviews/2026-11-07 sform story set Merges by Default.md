# sform story set Merges by Default

## Overview

This review covers all of [sform story set Merges by Default](../2026-11-07%20sform%20story%20set%20Merges%20by%20Default.md), in the branch of [the first fix](2026-11-07%20sform%20story%20set%20Sends%20an%20Incomplete%20Annotation%20Body.md) (PR #169). On an existing key, `sform story set` now reads the annotation and merges the file and the options into it, as `sform story config set` does. `--replace` keeps the behaviour of the first fix. It was not run against a live API.

| Commit | Content |
| --- | --- |
| `bc1c04d9` | The spec |
| `90177976` | `--replace`, the read, the merge in `ToSdkAnnotation`, and the tests |
| `c5bc58e7` | `annotation-lifecycle.md`, `docs/42for.net/platform/annotations.md`, and a pointer from the first review |
| This commit | This review |

## What Was Done

### 1. `--replace`

`IsReplaceRequested` on `StorytellerSetCommand`, with the help of the spec. With it, the command reads nothing, and the body is the input alone.

### 2. The merge

- **The read.** `ReadStoredAsync` calls `GetAnnotationAsync(…, includeInactive: true)` after `HandleDeletedKeysAsync`. A `404` gives no stored annotation.
- **The body.** `ToSdkAnnotation(stored)` follows the spec's table, member by member. `CreateForKey` takes a unit's type and definition from the input, else from the stored unit, else from the defaults. Labels are joined without duplicates, which also removes a duplicate label of a file under `--replace`.
- **Values** are copied from the stored annotation as the SDK read them, so date-like strings keep their text.

### 3. Failures

The read sits in the command's `try`, so its failures go through the handlers of the write. A `403` prints the server's message and exits with `44`, and the write is not sent.

### 4. Help and documentation

As planned. The argument's help now says "to create or update". The row in `annotation-lifecycle.md` lists `--replace` and the other options, and says that a member cannot be cleared in a merge. It also names the lost update between the read and the write.

### 5. Tests

- **`StorytellerSetBodyTests`**, now 40 tests, script the read, a `404` unless a test gives a stored annotation. They cover:
  - a merge with the options alone, and one with a file and the options over a stored execution;
  - a stored unit, and a unit of the file over it;
  - `--replace`, which reads nothing;
  - a refused read, which sends no write.
- **`StorytellerSetCommandTests`** set up the read, and check the order: restore, read, write. **Create new** reads too: in the mocked test the read answers the key, while the real API answers `404` for a key that is still deleted.

### 6. Verification

| Check | Result |
| --- | --- |
| `dotnet test --project src/Platform/Cli/test` | 1102 passed |
| The built `sform` against a throwaway fake that answers one stored execution | The default merged the stored title, labels and values, a date-like value included, with `-l` and `-c`. `--replace` sent the input alone without a read. A new key was read, answered `404`, and got the input alone |

**Not run:** against a live API and Cosmos DB, and the read after a real restore.
