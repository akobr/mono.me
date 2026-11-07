# sform story set Sends an Incomplete Annotation Body

## Overview

This review covers all of [sform story set Sends an Incomplete Annotation Body](../2026-11-07%20sform%20story%20set%20Sends%20an%20Incomplete%20Annotation%20Body.md). `sform story set` now writes an annotation of every type: its body has `AnnotationKey` and the type-specific members the API requires. A review of the change then found four more defects in the command, which every run had been crashing before it could reach, and they are fixed too (see [Added by the review](#added-by-the-review)).

**Not run against a real platform.** The bodies went through the real handler of the API, in a one-off test, but not through a running API and Cosmos DB. See [Verification](#6-verification).

| Commit | Content |
| --- | --- |
| `0d7ee5fa` | The spec |
| `5d7673f1` | The body, the unit's defaults in `Unit`, the fixes added by the review, the command's help, and `StorytellerSetBodyTests` |
| `e7716bc0` | `annotation-lifecycle.md` and `docs/42for.net/platform/annotations.md` |
| This commit | This review |

**Decided with the owner:** `sform story set` keeps replacing the content of an existing key, as the API's `PUT` does (spec section 3).

## What Was Done

### 1. The body

`ToSdkAnnotation` (`src/Platform/Cli/src/Model/AnnotationExtensions.cs`) parses the key and builds the SDK's model of its type in a new `CreateForKey`, with the members of the spec's table from `AnnotationKeyExtensions`, and sets `AnnotationKey`. The relation arrays stay out. An unknown type now throws instead of falling back to a base model; it cannot happen, as the key has been parsed.

### 2. A unit's own members

`UnitType` and `UnitDefinition` come from the `Unit` of a file, or else from `Unit.DefaultUnitType` and `Unit.DefaultUnitDefinition`, new constants in `Abstractions.Annotations`. `AnnotationFactory`'s constants now refer to them, so its callers did not change.

### 3. The meaning of a write stays

As planned: the body replaces the content of an existing key. The command's description and `annotation-lifecycle.md` now say so.

**Correction of the spec.** Its list of what is cleared leaves out the documentation link and the time zone, and "without `--import`" is too narrow: a file that leaves a member out clears it too. `annotation-lifecycle.md` has the full list.

### 4. Tests

`StorytellerSetBodyTests` runs the command with the real `AnnotationsApiClient` over `ScriptedHttpHandler`, 34 tests. Before the fix they failed with the error of the spec's Problem. Besides the spec's list, they cover every editable member of a file and the cases of [Added by the review](#added-by-the-review).

### 5. Documentation

- **`docs/Platform/Storyteller/annotation-lifecycle.md`:** the row of `sform story set`.
- **`docs/42for.net/platform/annotations.md`:** its sentence about restoring promised the old record back. Under the replace semantics only the configuration and the history come back, so it now says so, and names `sform story restore`. This page was not in the spec.
- **The command's help:** `-c` and `-l` said they act "on the configuration", and the argument said "to get". They now describe what the command does.

### 6. Verification

| Check | Result |
| --- | --- |
| `dotnet test --project src/Platform/Cli/test` | 1096 passed. The new tests failed before the fix |
| `Backend.Core` and `Api.Functions` unit tests | 1231 and 2752 passed |
| The built `sform` against a throwaway fake, with `DO_NOT_TRACK=1` | All seven types, a unit with a file, and a file shaped like a saved read exit with `0` and send the expected bodies. So do a date-like value with a `-c` over a name of the file, and an `EXE.` key. A broken file exits with `2` |
| Those first nine bodies through the real `AnnotationsHttp.SetAnnotation`, `AnnotationRules.Validate` and `AnnotationFactory.Normalize` | Accepted, in a one-off test that is not committed. It ran before the review's changes, which did not change the shape of those bodies |

**Not run:** a write against a running API and Cosmos DB, as the local platform holds the developer's data and `sform` needs an interactive sign-in. Creating the ancestors and `UnitNames` on the server is covered by the backend's own tests, not by a run of this command.

## Added by the review

Reviewers found these by running the command with inputs the first tests did not have, and a separate reader confirmed each one. All are older than this fix, but every run crashed before reaching a write, so nothing had exposed them. Each now has a test.

1. **`--import` rewrote date-like strings.** The file was read with Newtonsoft's default date parsing, so `2026-10-24T10:00:00+05:30` in `Values` was written back in the machine's offset, and a date-like title in the current culture's format. A file saved from a read no longer held the same text. The reader now uses `DateParseHandling.None`, as the SDK's reads do. `ValidFrom` and `ExpiresAt` still parse, as they are typed.
2. **`-c` with a name of the file crashed.** `new Dictionary(Concat(…))` threw on the duplicate. `-c` now wins.
3. **A file that is no annotation ended as a crash.** Newtonsoft's exceptions reached `Program`'s "the tool is broken". The command now catches `JsonException` and exits with `2`, with the file name and the reader's message on standard error. It uses `WriteErrorLine`, not `WriteImportant`: see the next section.
4. **A type code in another case wrote a second annotation.** `sform story set EXE.n.b.p` looked up and wrote `EXE.n.b.p`, and the API stores an annotation under its key as written. The command now lower-cases the type code first. This was shown with the real client and annotation service, not with Cosmos DB.

## For the owner

1. **Replace or merge.** `sform story set sbt.northwind -l eu` on an existing key now clears its title, description, documentation link, validity window, time zone, disabled flag, and other values and labels, and a unit goes back to `event` and `unknown`. That matches the API's `PUT` and the import, and it is stated in the help and the docs. But no write reached the server before, so this is the first time anyone can hit it. Reading the key first and merging would be a change of the command's meaning, so it is left for a decision.
2. **The API accepts a type code in another case.** The command now sends a canonical key, but any other client can still create `EXE.n.b.p` next to `exe.n.b.p`. Canonicalizing in the API's write would close that for every client.
3. **`WriteImportant` crashes when standard output is redirected on Windows.** Alba's `ConsoleRenderer` throws `IOException: The handle is invalid`, which ends as "the tool is broken". This was seen with `*>` in PowerShell, on the broken-file message before it moved to `WriteErrorLine`. `WriteImportant` appears 142 times in `src/Platform/Cli/src`, so other messages can do the same in a script. They were not changed or tested here.
