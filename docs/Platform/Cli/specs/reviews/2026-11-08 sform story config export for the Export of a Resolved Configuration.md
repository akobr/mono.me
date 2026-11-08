# sform story config export for the Export of a Resolved Configuration

## Overview

`sform story config <key>` now only reads a configuration. Handing a resolved configuration to a CI job moved to a new subcommand, `sform story config export <key>`. It always reads the resolved configuration and always masks. Its options are `--github-env`, `--mask`, `--prefix` (`--env-prefix` renamed), `--allow-unprefixed`, `--allow-unresolved`, and `-e|--export` with `-f|--format` (`env` by default). The guarantees of the old `--github-env`/`--mask` path still hold:

- the `::add-mask::` lines come first, then `GITHUB_ENV`, then the file;
- every check runs before anything is written;
- withheld secrets are refused;
- every string and number is masked when the effective read fails or returns another version;
- the summary goes to standard error and never holds a value.

`story config` no longer takes `--github-env`, `--mask`, `--allow-unprefixed` or `--allow-unresolved`. There are no aliases: the parser refuses them (exit `-1`). The GitHub Action calls the new command and has a new input, `allow-unresolved`. This review covers the three phases of [sform story config export for the Export of a Resolved Configuration](../2026-11-08%20sform%20story%20config%20export%20for%20the%20Export%20of%20a%20Resolved%20Configuration.md) and the fixes of the code review. Linear: [424-220](https://linear.app/424fun/issue/424-220/sform-move-the-github-and-masking-export-out-of-story-config-into).

## What Was Done

### 1. `ConfigExportCommand`

New file `src/Platform/Cli/src/Commands/Configuration/ConfigExportCommand.cs`, as specified. It is registered last in the `[Subcommand(...)]` list of `ConfigGetCommand`, depends on `GitHubActionsSettings` (already registered in `Startup.cs`), and inherits `-p` and `-v` from `BaseContextCommand`.

- **Options:** the options and descriptions of the spec. `sform story config export --help` lists them, and `sform story config --help` lists `export` among its commands.
- **Validation:** the ten checks of the spec, in its order and with its messages. Every message goes to standard error as one plain line, the key message too. A wrong command line never calls the API and never writes a file.
- **Execution:** `ExportAsync` of the old `ConfigGetCommand`, moved as it was. The only changes are its inputs:
  - `IsMasking` is gone;
  - the read options carry a `MaskMode`, which is never null and is `Bound` by default;
  - the prefix is `Prefix`;
  - the file is always resolved content, so it is written with `OwnerOnlyFile` on Unix.

  `AppendToEnvironmentFile` moved with it unchanged.
- **Default format:** `env` when `--format` is missing. `json` (any case) is the JSON content alone, as `--content-only` wrote it before.

### 2. `ConfigGetCommand` keeps the read job

As specified. The file shrank from 643 to 241 lines.

- `IsPlainOutput` is `IsContentOnly`.
- The constructor no longer takes `GitHubActionsSettings`.
- The only changed message is "--env-prefix applies to --format env only.". It used to name `--github-env` too.
- Every read command line behaves as before: the same reads, messages, output routing, warnings and files. The read tests that moved over unchanged prove it.

### 3. The shared helper `ConfigurationReads`

New file `src/Platform/Cli/src/Commands/Configuration/ConfigurationReads.cs`, with the signatures of the spec:

- `ReadAsync`, `ReadEffectiveAsync`;
- `TryParseFormat`, `IsValidPrefix`;
- `ToToken`, `ToContentJson`, `Paths`;
- `WriteMessage`, `WriteReadFailure`;
- `TryFormat`, `WriteInvalidDocument`;
- `WriteFile`.

`ResultOf` is private to it, and `ReadEffectiveAsync` goes through `ReadAsync`. The version read and `WarnOfUnresolvedAsync` stay in `ConfigGetCommand`. `MaskMode`, `MaskPlan` and `ConfigurationMasks` did not change.

### 4. Telemetry

No change. A run reports `sform storyteller config export`.

### 5. The command tree and a key named `export`

As specified. A test parses `story config export <key>` and `story configuration export <key>` through `StorytellerListCommand` and gets a `ConfigExportCommand` with the key. The docs note that `export` is never read as a key, the same as `set` or `versions`.

### 6. Tests

`dotnet test --project src/Platform/Cli/test` passes: 1119 tests, none failed or skipped.

- **`ConfigGetCommandTests`** (79 cases) keeps the read job.
  - `InvalidCommandLines` drops the export rows.
  - It gains two rows for `--env-prefix` without `--format env`: one with `-r`, one in `json` with `-e`.
  - `CommandNames` keeps its two read rows.
  - `Options_AreParsed` covers only the read options.
  - New: `RemovedExportOptions_DoNotParse`. `--github-env`, `--mask all`, `--mask`, `--allow-unprefixed`, `--allow-unresolved` and `--prefix` each throw an `UnrecognizedCommandParsingException`.
  - The test no longer registers `GitHubActionsSettings`: `ConfigGetCommand` does not need it.
- **`ConfigExportCommandTests`** (82 cases) is new.
  - Every export case of the old class is here, without `-r` and `--content-only`, with `--prefix` for `--env-prefix`, and with the same expectations. Where the old test wrote JSON with `--content-only`, it now passes `-f json`. Two cases now cover more than before:
    - `UnresolvedBindings_StopAnExport_NamingThePaths` also runs with `-e app.env` alone.
    - `GitHubEnv_RefusesNamesThatSteerLaterSteps_AndWritesNothing` also passes `-e app.env` and checks that no file appears.
  - New cases:
    - `InvalidCommandLines`: 17 rows, at least one for each message of section 1 of the spec;
    - the default format of the file with `--prefix`, each text format, `json` in either case, and a yaml file next to prefixed variables;
    - `--mask` alone;
    - only the resolved and the effective read;
    - the `404`, `422`, `500`, `401` and `403` of the resolved read;
    - `--mask all` with a failed effective read;
    - an `env` file that the formatter refuses;
    - two `CommandNames` rows for the invalid document, as variables and as a file;
    - `Options_AreParsed` with the `--option=value` form of the action;
    - `OptionsOfTheRead_DoNotParse` (`-r`, `--resolved`, `--version 2`, `--content-only`, `--env-prefix APP_`);
    - the command tree test of section 5.
  - From the code review: `GitHubEnv_ThatCannotBeWritten_IsWrongInput_AndTheFileIsNotWritten` and `File_ThatCannotBeWritten_IsWrongInput_AfterGitHubEnvWasAppended`. Together they pin the order of `GITHUB_ENV` and the file (see Code review).
- **`ProgramTests`** (16 cases):
  - `config <key>` with `--mask`, `--github-env` or `--allow-unprefixed` ends with `ERROR_INPUT_PARSING`;
  - a new theory does the same for `config export <key>` with `--mask` without a value, `--prefix` without a value, a format given twice, `--resolved`, `--env-prefix APP_` and `--bogus`;
  - the missing-value test is a theory over `env-prefix`, `prefix` and `mask`.
- **`StorytellerTransferCommandTests.Commands_AreSubcommandsOfStory_AndNoLongerOfConfig`** checks types now. `config` holds neither `StorytellerExportCommand` nor `StorytellerImportCommand`, its only `export` is `ConfigExportCommand`, and no subcommand is named `import`.
- `ConfigCompositionCommandTests` was not changed.

### 7. The GitHub Action

As specified, in `src/Platform/GitHubAction/action.yml` and `README.md`. The new input `allow-unresolved` defaults to `'false'`, is checked as a boolean, and passes `--allow-unresolved`. The read step builds `story config export "$SA_KEY" "--mask=…"` with `--prefix=` where it passed `--env-prefix=` before, and without `--resolved` or `--content-only`.

- **Check:** a script extracted both steps from the old and the new `action.yml` and ran them in Git Bash against a stub `sform` that prints its arguments. It covered every combination of `export-env`, `env-prefix`, `allow-unprefixed`, `mask`, `output-file` and `format` (360 in all).
  - **Check the inputs** gave the same exit code and output in every combination.
  - **Read the configuration** built the old command line mapped to the new options (`--mask X` to `--mask=X`, `--env-prefix=` to `--prefix=`, no `--resolved` or `--content-only`) in every combination.
  - Every command line it builds passes the validation of `ConfigExportCommand`. A `--format` comes only with `--export`, a prefix only with `--github-env` or an `env` file, and `--allow-unprefixed` only with `--github-env`.
  - `allow-unresolved: yes` fails the check with "The input allow-unresolved must be true or false.". A `mask` of `@resp` reaches `sform` as `--mask=@resp`, which is not a response file.
- **README:** the intro, the inputs table, steps 1 and 4, a sentence on the `--option=value` form, the unresolved bindings, **Until sform is published** (pack `sform` from the commit of the action or a newer one), and `ConfigExportCommandTests` in **Checks**. The link for the exit codes now points to **Export to GitHub Actions**.

### 8. Documentation

- **`docs/42for.net/cli/sform.md`:**
  - The warning of "Sign in from a pipeline" points to `story config export`.
  - The `secrets-read` row of the scope table names `story config export`.
  - "Read a configuration" keeps the read synopsis, the output table without the masking row, the plain-output rules of `--format` and `--content-only`, and its exit codes. Its `-1` row names the options of `config export`.
  - "Export to GitHub Actions" (the heading stays for its links) describes `story config export`: the synopsis, an options table, the targets, the output, the key named `export`, the steps, its own exit codes, and the examples with `--github-env --prefix APP_` and `-e "$RUNNER_TEMP/billing.env"`.
- **`docs/42for.net/cli/github-action.md`:** the intro, `allow-unresolved` in the inputs, step 4 of "What the action does", the link for the exit codes, the unresolved bindings, and the commit to pack `sform` from.
- **`docs/Platform/Storyteller/`:** in `authentication.md`, the CI example and its warning, and the federated-machine paragraph. Also `configuration-formats.md`, `templating.md`, and the note in `export-import.md` that `config export` is now another command.
- **`CLAUDE.md`:** the row of `GitHubAction/`.

### 9. Out of scope

All as specified. `docs/Platform/Cli.AutomationTests/test-cases.md` is still only uncommitted work in the main checkout. Whoever commits it needs these changes:

- **`CFG-GHA-01`:** `config export <rst> --github-env --prefix APP_`, without `-r`.
- **`CFG-GHA-02`:** `config export <rst> --github-env` fails with exit `2`, naming `--prefix` and `--allow-unprefixed`; then it runs with `--allow-unprefixed`.
- **`CFG-GHA-03`:** `config export <rst> --mask bound`, then `--mask all`.
- **`CFG-GHA-04`:** `config export <rst> --github-env --prefix A_`, then the same with `--allow-unresolved`.
- **`CFG-GHA-05`:** no longer possible: the export is always resolved. One replacement: `config <rst> --github-env --env-prefix A_` exits `-1` (an unrecognized option), and `config export <rst>` without a target exits `2` with "Nothing to export: …".
- **`GEN-CLI-05`:** `config export <rst> --github-env --prefix A_` instead of `config -r --github-env`.
- The section title "GitHub Actions and Masking" and the class `ConfigGitHubTests` may stay. The "Covers" column names `--prefix` instead of `--env-prefix`.
- `CFG-GET-07` (`--env-prefix` with `-f env`) still holds.

### Code review

A workflow of three reviewers ran on the branch: the code against the old `ConfigGetCommand`, the tests against the old test class, and the action and docs against the code. Each reviewer's findings went to an adversarial verifier. Three findings were confirmed and fixed in `5dc85bbb`:

- **The docs said that standard output of the export holds only the masks.** Two reviewers reported it. That holds only for what the command writes itself. `Program` still prints parsing errors, sign-in failures, `401`/`403` refusals and crashes to standard output, as before. The old docs had a caveat for `--github-env`/`--mask`, which the move dropped. The export section of `sform.md` has it again.
- **No test pinned the order of `GITHUB_ENV` and the file.** `FirstWriteProbe` only proves that the masks come first. The code was right. Two new tests now fail if the two writes are swapped, or if the file is written after a failed `GITHUB_ENV`:
  - a `GITHUB_ENV` in a folder that does not exist gives exit `2`, the masks, "GITHUB_ENV could not be written: …", and no file;
  - a folder at the path of the file gives exit `2`, a `GITHUB_ENV` that holds the variables, and "The configuration could not be written to 'app.env': …".

Five findings were refuted. Each was either decided by the spec or needed a change to the code that nobody made:

- no test of the validation order for lines that break two rules;
- no negative test of the `GITHUB_ENV` check without `--github-env`;
- no test of the version `404` message through the helper;
- the prose of `configuration-formats.md`, which names `--github-env` without `--prefix`;
- the review not written yet. It is this file.

### Commits

1. `0234582e` the spec.
2. `278973c1` the CLI and its tests (phase 1).
3. `d7ad330c` the GitHub Action (phase 2).
4. `00590265` the documentation (phase 3).
5. `5dc85bbb` the fixes of the code review.
6. This review.
