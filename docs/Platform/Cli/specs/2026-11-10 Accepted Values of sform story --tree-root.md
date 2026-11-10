# Accepted Values of sform story --tree-root

Linear: [424-219 sform story --tree-root: reject unknown values and accept subject explicitly](https://linear.app/424fun/issue/424-219/sform-story-tree-root-reject-unknown-values-and-accept-subject).

Line numbers refer to the tree this plan was written against (`2c91d261b`). The issue has no comments and no CodeRabbit plan.

## Problem

`sform story --tree --tree-root <type>` takes any value. Only `responsibility` (or its type code `rst`) changes the output. Every other value quietly renders the tree of subjects, so a typo (`--tree-root responsibilty`) or a type that has no tree (`--tree-root context`) looks like it worked. `subject` is not an accepted value either: it only works because everything unknown falls back to the subject tree.

`--tree-root` without `--tree` is ignored altogether: `sform story -b rst` prints the plain list of annotations.

The help does not say which values the option takes: "An annotation type to render the tree based on."

## Current State

All paths are relative to `src/Platform/Cli/` unless they start with `docs/`.

### The command

`src/Commands/Storyteller/StorytellerListCommand.cs` (`sform storyteller`, aliases `story` and `annotations`):

- Line 65: `[Option("-t|--tree", NoValue, "Show results as tree based by subjects.")] ShowAsTree`.
- Line 68: `[Option("-b|--tree-root", SingleValue, "An annotation type to render the tree based on.")] string? TreeBaseAnnotationType`.
- Line 90, `ExecuteAsync`: when `ShowAsTree` is set it calls `RenderTreeAsync` and returns `SUCCESS`; otherwise `-b` is never read.
- Line 391, `RenderTreeAsync`: compares the value case-insensitively with `"responsibility"` and `AnnotationTypeCodes.Responsibility` (`rst`) and calls `RenderResponsibilityTreeAsync`; anything else, the missing value included, calls `RenderSubjectTreeAsync`.

### How other commands refuse a value

The commands of the `story` area check a value with a fixed set in code and end with `ExitCodes.ERROR_WRONG_INPUT` (2) and a line on stderr (`Console.WriteErrorLine`) before any call: `ConfigGetCommand` ("Unknown format '<x>'; use json, flat, properties, env or yaml."), `ConfigExportCommand.Validate`, and `AccountSettingsEditorCommand` ("'<x>' is no editor sform knows; use …").

McMaster's `[AllowedValues(..., IgnoreCase = true)]` would also refuse the value, but through the default validation error handler, which exits with `1`. In sform `1` is `ERROR_CRASH`, and the CLI test catalogue treats it as a crash that fails every case. Its message would not name the type codes as the codes of the types either.

### Tests and docs

- `test/Commands/StorytellerListCommandTests.cs`: unit tests of the list (strict `IAnnotationsApiClient` mock, `AnnotationCommandFixture`); nothing about the tree.
- `src/Platform/Cli.AutomationTests/src/Tests/Annotations/AnnotationListTests.cs`, `Story_Tree_Rendered` (case `ANN-LST-06`, Testiny 255): `story -t`, `story --tree -b sbt`, `story -t --tree-root rst`, `story -t -b responsibility`; `[Covers("story", "-t", "--tree", "-b", "--tree-root", "--tree-root=sbt", "--tree-root=rst", "--tree-root=responsibility")]`.
- `src/Platform/Cli.AutomationTests/src/coverage-values.json`: the entry of `storyteller --tree-root` lists `rst`, `responsibility`, `sbt`, with the source "anything else (sbt) the tree of subjects". `CommandCoverageTests` wants each listed value in a `[Covers]`.
- `docs/Platform/Cli.AutomationTests/test-cases.md`, row and steps of `ANN-LST-06`: "`-b` takes effect with `-t` only, and any other value renders the subject tree." `CatalogueTests` compares the row's Covers with the test's `[Covers]`.
- `src/Properties/launchSettings.json` runs `story --tree --tree-root responsibility`; it stays valid.
- No other document under `docs/` (outside `specs/`) mentions `--tree-root` or `--tree`.

## Proposed Changes

### 1. The accepted values

The values of `--tree-root`, compared without case and with the surrounding white space trimmed:

| Value | Tree |
| --- | --- |
| `subject`, `sbt` | Tree of subjects (subject → context → responsibility) |
| `responsibility`, `rst` | Tree of responsibilities (responsibility → subject → context) |

Without `--tree-root` the tree is the tree of subjects, as today.

A new enum `StoryTreeRoot { Subject, Responsibility }` in `src/Commands/Storyteller/StoryTreeRoot.cs` names the two trees. `StorytellerListCommand` gets:

```csharp
// The tree --tree-root asks for: the subjects when it is not given.
internal static bool TryParseTreeRoot(string? value, out StoryTreeRoot root)
```

It returns `true` with `Subject` for `null`, `true` with the tree of one of the four values, and `false` for anything else, an empty or blank value included (`--tree-root=` is a mistake, not a request for the default).

`RenderTreeAsync` takes the parsed `StoryTreeRoot` and switches on it, so the string comparison goes away.

### 2. Refusing any other value

`ExecuteAsync` checks the value first, before any call to the API:

```text
Unknown tree root 'context'; use subject (sbt) or responsibility (rst).
```

on stderr (`Console.WriteErrorLine`), and exit code `ExitCodes.ERROR_WRONG_INPUT` (2), as `story config` does for an unknown format. The message lists the four accepted values.

The value is checked whenever it is given, with or without `--tree`.

### 3. `--tree-root` without `--tree`

`--tree-root` implies `--tree`: `sform story -b rst` renders the tree of responsibilities. The option only means something for a tree, so giving it is asking for a tree. A warning that tells the user to add `-t` would only make them type it again.

Nothing else changes: with neither option the command lists as before, and `--tree` with the queries (`-r`, `-s`, `-c`) filters the tree as before.

### 4. Help texts

- `-t|--tree`: "Show the results as a tree, of subjects unless --tree-root names another root."
- `-b|--tree-root`: "The root of the tree: subject (sbt, the default) or responsibility (rst). Implies --tree."

### 5. Tests

Unit tests in `test/Commands/StorytellerListCommandTests.cs` (the API mock stays strict, so an unexpected call fails):

- `--tree` alone asks for the subjects (`GetSubjectsAsync`), not the responsibilities.
- `--tree-root` with `subject`, `sbt`, `SUBJECT`, ` Sbt ` renders the tree of subjects; with `responsibility`, `rst`, `Responsibility`, `RST` the tree of responsibilities (`GetResponsibilitiesAsync`).
- `--tree-root` with `context`, `cnt`, `subjects`, `responsibilty` and an empty value ends with exit 2, the message on stderr, and no call to the API, with and without `--tree`.
- `--tree-root rst` without `--tree` renders the tree of responsibilities.
- The help of `story` names the accepted values in the description of `--tree-root`.

The CLI automation test of `ANN-LST-06` (`Story_Tree_Rendered`) follows the catalogue:

- `story -t --tree-root subject` joins `story -t` and `story --tree -b sbt`, which print "Tree of subjects".
- `story -b rst` (no `-t`) joins `story -t --tree-root rst` and `story -t -b responsibility`, which print "Tree of responsibilities".
- `story -t -b context` ends with exit 2, "Unknown tree root 'context'; use subject (sbt) or responsibility (rst)." on stderr, and prints no tree.
- `[Covers]` adds `--tree-root=subject`.

`coverage-values.json`: the `storyteller --tree-root` entry lists `sbt`, `subject`, `rst`, `responsibility`, with the new source (`StorytellerListCommand.TryParseTreeRoot`, another value ends with exit 2).

### 6. Documentation

- `docs/Platform/Cli.AutomationTests/test-cases.md`: the row of `ANN-LST-06` (Covers with `--tree-root=subject`) and its steps and expectations as in §5.
- The Testiny case 255 follows the catalogue, which is the source of truth for it (`automation-tests.md`); updating it in Testiny is a step outside the repository.

### 7. Out of scope

- Trees rooted at other types (contexts, usages, executions). The error lists the two roots that exist.
- The other options of `story` that are ignored in some combinations (for example `-d` with `-r`).

### 8. Phases and commits

One phase:

1. `docs(platform)`: this spec.
2. `fix(platform)`: the command, its unit tests, the automation test, `coverage-values.json` and the catalogue.
3. `docs(platform)`: the review.
