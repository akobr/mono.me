# Accepted Values of sform story --tree-root

## Overview

This review covers all of [Accepted Values of sform story --tree-root](../2026-11-10%20Accepted%20Values%20of%20sform%20story%20--tree-root.md) (Linear 424-219), delivered in one phase. `sform story --tree-root` now takes `subject` or `sbt` (the default) and `responsibility` or `rst`, in any case. Any other value ends with exit code 2 and a message that lists the accepted values, before any call to the API. The option implies `--tree`, and the help of both options names the values. The CLI test case `ANN-LST-06`, its automated test, `coverage-values.json` and the catalogue follow. Nothing was run against a live API.

| Commit | Content |
| --- | --- |
| `f5d51e4c` | The spec |
| `ec892336` | The command, its unit tests, the automated test of `ANN-LST-06`, `coverage-values.json` and the catalogue |

## What Was Done

### 1. The accepted values

- New `src/Platform/Cli/src/Commands/Storyteller/StoryTreeRoot.cs`: `public enum StoryTreeRoot { Subject = 0, Responsibility }`.
- `StorytellerListCommand.TryParseTreeRoot(string? value, out StoryTreeRoot root)` (`internal static`, placed before `ExecuteAsync` for StyleCop's order of members). It trims the value, lowers its case and switches on it: `null`, `subject` and `AnnotationTypeCodes.Subject` give `Subject`; `responsibility` and `AnnotationTypeCodes.Responsibility` give `Responsibility`; anything else returns `false`. That includes an empty or blank value.
- `RenderTreeAsync(StoryTreeRoot root)` switches on the parsed root. The old string comparison is gone.

### 2. Refusing any other value

`ExecuteAsync` checks the value first and writes this line to stderr (`Console.WriteErrorLine`):

```text
Unknown tree root '<value>'; use subject (sbt) or responsibility (rst).
```

It then returns `ExitCodes.ERROR_WRONG_INPUT` (2), whether or not `--tree` is given. The value is printed as typed (`WriteErrorLine` replaces control characters). As planned, McMaster's `[AllowedValues]` was not used: its validation handler exits with 1, which sform reserves for a crash.

### 3. `--tree-root` without `--tree`

`ExecuteAsync` renders the tree when `ShowAsTree` is set **or** `TreeBaseAnnotationType` is given. So `sform story -b rst` prints the tree of responsibilities, and `sform story -b subject` the tree of subjects. Without either option the command lists as before.

### 4. Help texts

As planned:

- `-t|--tree`: "Show the results as a tree, of subjects unless --tree-root names another root."
- `-b|--tree-root`: "The root of the tree: subject (sbt, the default) or responsibility (rst). Implies --tree."

### 5. Tests

`src/Platform/Cli/test/Commands/StorytellerListCommandTests.cs` has 25 new test cases. The API mock is strict, so an unplanned call fails a test.

- `Tree_WithoutRoot_IsTheTreeOfSubjects`: `--tree` alone asks for the subjects.
- `Tree_WithSubjectRoot_IsTheTreeOfSubjects` (`subject`, `sbt`, `SUBJECT`, ` Sbt `) and `Tree_WithResponsibilityRoot_IsTheTreeOfResponsibilities` (`responsibility`, `rst`, `Responsibility`, `RST`).
- `TreeRoot_WithoutTree_RendersTheTree` (`rst`, `subject`): the tree is rendered, and the plain list is never asked for.
- `TreeRoot_Unknown_IsRefusedWithTheAcceptedValues`: the values `context`, `cnt`, `subjects`, `responsibilty`, an empty value and a blank one, with and without `--tree`. Each ends with exit 2, the single line on stderr, nothing on stdout, and no API call (`VerifyNoOtherCalls`).
- `TryParseTreeRoot_TakesOnlyTheTwoRoots`: the parser on its own, `null` included.
- `Help_NamesTheValuesOfTreeRoot`: the description of `-b|--tree-root` in the command model McMaster builds.

The automated test of `ANN-LST-06` (`AnnotationListTests.Story_Tree_Rendered`) also does the following:

- It runs `story -t --tree-root subject` among the steps that print the subject tree.
- It runs `story -b rst` (no `-t`) among the steps that print the responsibility tree.
- It ends with `story -t -b context`: exit 2 (`CliExit.WrongInput`), the message on stderr and no "Tree of" on stdout.
- Its `[Covers]` adds `--tree-root=subject`.

`coverage-values.json` lists `sbt`, `subject`, `rst`, `responsibility` for `storyteller --tree-root`, with `TryParseTreeRoot` and the exit 2 in its source.

Verification:

- `dotnet test --project src/Platform/Cli/test`: 1943 passed.
- The checks of `cli-checks.yml` (`--treenode-filter "/**[(Category=SelfTest)|(Category=Coverage)]"`): 87 passed. That includes `CommandCoverageTests` and `CatalogueTests` against the new value and the catalogue row.
- The built CLI with an empty `SFORM_HOME`:
  - `story -t -b context` and `story -b Responsibilty` print the message and exit 2 without signing in.
  - `story --help` shows both new descriptions.
- `Story_Tree_Rendered` itself was not run: it needs a deployed target.

### 6. Documentation

- `docs/Platform/Cli.AutomationTests/test-cases.md`:
  - The row of `ANN-LST-06` lists `--tree-root=subject` in Covers.
  - Its steps add `story -t --tree-root subject`, `story -b rst` and `story -t -b context`.
  - Its expectation explains that `-b` implies `-t` and refuses any value but the four, with exit 2 and the message.
- No other document outside `specs/` mentions the options. `launchSettings.json` (`story --tree --tree-root responsibility`) stays valid.
- Testiny case 255 (project `2SC`) follows the catalogue: its precondition lists `--tree-root=subject` in Covers, and its seven steps are those of the row, the last one the refused `story -t -b context`.

### 7. Out of scope

As planned: trees rooted at other types, and the other options of `story` that are ignored in some combinations.
