# sform story config export for the Export of a Resolved Configuration

Linear: [424-220 sform: move the GitHub and masking export out of `story config` into `story config export`](https://linear.app/424fun/issue/424-220/sform-move-the-github-and-masking-export-out-of-story-config-into)

## Problem

`sform story config <key>` does two jobs with one set of options:

1. **It reads and shows a configuration:** the model, `-r|--resolved`, `--version`, `-f|--format`, `--content-only`, `-e|--export` and `--env-prefix`.
2. **It hands a resolved configuration to a CI job:** `--github-env`, `--mask`, `--env-prefix`, `--allow-unprefixed` and `--allow-unresolved`.

The help output lists both sets of options. Most of the validation only rejects combinations from the wrong job: masking needs `--resolved` and refuses `--version`, `--content-only` with masking needs `--export`, the `--allow-*` options need masking, and `--env-prefix` needs `--format env` or `--github-env`. The two jobs also run on separate code paths of one class (`WriteAsync` and `ExportAsync`).

The issue moves the second job into a subcommand of its own, `sform story config export <key>`. It always reads the resolved configuration and always masks. The options leave `story config` without aliases and without a deprecation period: nobody uses them outside this repository, and the callers in the repository change in the same change.

CodeRabbit wrote a coding plan on the issue. Only its summary is on the issue, and it agrees with the issue: a `ConfigExportCommand` child of `ConfigGetCommand`, one internal static helper for what both commands share, split tests with rejection tests for the removed options and tests of the command tree, and an opt-in `allow-unresolved` input of the action. This spec follows it on all four points. It adds what the summary leaves open: the exact validation of the new command, the default format of its file, the `--option=value` form for `--mask` in the action, and the test of the command tree that says `config` has no `export` (section 6).

## Current State

All paths are relative to `src/Platform/` unless they start with `docs/`.

### `ConfigGetCommand`

`Cli/src/Commands/Configuration/ConfigGetCommand.cs` (643 lines) is `sform story config` (`config`, `configuration`). It is a subcommand of `StorytellerListCommand` (`sform story`), and its own `[Subcommand(...)]` list holds `set`, `edit`, `patch`, `delete`, `diff`, `versions` and `composition`. It depends on `IConfigurationsApiClient`, `IFileSystem` and `GitHubActionsSettings`.

- **Options (lines 54-87):** the argument `AnnotationKey`, `-e|--export`, `-r|--resolved`, `--version`, `-f|--format`, `--content-only`, `--env-prefix`, `--github-env`, `--mask`, `--allow-unprefixed` and `--allow-unresolved`.
- **`IsMasking` (line 89):** `--github-env` or `--mask`. `IsPlainOutput` (line 92) is `--content-only` or masking: the payload goes to standard output as it is, and every message to standard error.
- **`ToContentJson` (line 95):** the content with two-space indentation and LF line ends, `{}` without content. It is `internal static`, and a test calls it.
- **`ExecuteAsync` (line 114):** `Validate`, UTF-8 output for anything but the model, the read, then the withheld secrets: with masking they stop the command, otherwise they are a warning. Then `ExportAsync` with masking, `WriteAsync` otherwise.
- **`Validate` (line 177):** the key (a plain line in plain output, `Console.ValidateAnnotationKey` otherwise), the format, the mask, `--version` with `--resolved`, masking without `--resolved` or with `--version`, `--content-only` with masking without `--export`, the prefix (where it applies and its characters), `--github-env` without a prefix or `--allow-unprefixed`, the `--allow-*` options without masking, and `GITHUB_ENV` not set.
- **`ReadAsync`, `ReadEffectiveAsync`, `ResultOf` (lines 170-305):** the stored content of a version, or the effective or the resolved read through the conditional twins (`GetConfigurationConditionalAsync`, `GetResolvedConfigurationConditionalAsync`), which keep the headers and with them `SecretsWithheld`.
- **`WriteReadFailure` (line 307):** `404` and `422` are wrong input with a sentence or the server's message and code. Any other status goes to `WriteApiErrorLine` in plain output and to `WriteApiError` otherwise. `401` and `403` never reach it: `Program` handles them.
- **`WriteAsync` (line 335):** the read job. A resolved read warns of unresolved bindings (`WarnOfUnresolvedAsync`, line 384, which reads the effective document only when a resolved string starts with `@`). Then the model, or the content as text, to the console or to the `--export` file.
- **`ExportAsync` (line 413):** the export job, with one resolved and one effective read. It computes everything first: the variables and their refusals (`GitHubEnvironmentFile.FindRefusals`), the payload of the file, the unresolved bindings (refused without `--allow-unresolved`), and the mask plan (`ConfigurationMasks.Plan`). The plan masks every string and number under `bound` when the effective read failed or returned another version. Only then does it write: the `::add-mask::` lines, `GITHUB_ENV` (`AppendToEnvironmentFile`), the file, and a summary on standard error that names variables and paths, never a value.
- **`TryFormat`, `WriteInvalidDocument`, `WriteExportFile`, `AppendToEnvironmentFile` (lines 566-640):** the payload in a format (the env prefix only for `env`), the two lines of a document a format cannot write, the file (`OwnerOnlyFile` with mode `0600` for a resolved read on Unix), and the append to `GITHUB_ENV` after a line feed when the job left its last line open.

`MaskMode.cs`, `MaskPlan.cs` and `ConfigurationMasks.cs` hold the masking. `Cli/src/GitHubActions/` holds `GitHubActionsSettings` (registered in `Startup.cs`, line 55), `GitHubEnvironmentFile` and `WorkflowCommands`. None of them changes.

### Tests

- `Cli/test/Commands/ConfigGetCommandTests.cs` (1045 lines) covers both jobs. It parses with `CommandLineApplication<ConfigGetCommand>` and constructor injection, and runs the selected model. Its `InvalidCommandLines` hold the validation messages of both jobs, and about thirty facts and theories cover `--github-env` and `--mask` (`GitHubEnv_*`, `Mask_*`, `WithheldSecrets_StopAnExport_*`, `UnresolvedBindings_StopAnExport_*`, and the second and fourth row of `CommandNames`).
- `Cli/test/ProgramTests.cs` parses `config <key>` with options that do not parse (`--mask` without a value among them) and checks `ERROR_INPUT_PARSING`.
- `Cli/test/Commands/StorytellerTransferCommandTests.cs`, `Commands_AreSubcommandsOfStory_AndNoLongerOfConfig` (line 43), checks that no subcommand of `config` is named `export` or `import`. It dates from the move of the view transfer, the `config export` of PR #88, to `sform story export`. That command was never released.
- `Cli/test/Commands/ConfigCompositionCommandTests.cs` parses through `ConfigGetCommand` and registers `GitHubActionsSettings`.

### The GitHub Action

`GitHubAction/action.yml` is a composite action. Its step **Check the inputs** fails on wrong inputs before anything is installed, and warns about an `env-prefix` that neither `export-env` nor an `env` file uses. Its step **Read the configuration** runs:

```bash
sform story config "$SA_KEY" --resolved --mask "${SA_MASK:-bound}" \
  [--github-env [--allow-unprefixed]] [--env-prefix=…] [--export=… --format=… --content-only]
```

It passes `--env-prefix` with `export-env`, or with `output-file` in the `env` format. It has no input for `--allow-unresolved`. `GitHubAction/README.md` describes the inputs and the steps with these options.

### Documentation

- `docs/42for.net/cli/sform.md`: the warning of "Sign in from a pipeline" (line 134), "Read a configuration" (line 143: the synopsis, the output table, the plain-output rules and the exit codes) and "Export to GitHub Actions" (line 186).
- `docs/42for.net/cli/github-action.md`: the intro, the inputs and "What the action does".
- `docs/Platform/Storyteller/authentication.md` (lines 492-509), `configuration-formats.md` (line 67) and `templating.md` (line 317) name `--github-env` and `--mask` of `story config`.
- `docs/Platform/Storyteller/export-import.md` (line 641) says `sform story config export` was removed.
- `CLAUDE.md` describes the action as running `sform story config <key> --resolved --github-env`.
- `docs/Platform/Cli.AutomationTests/test-cases.md`, which the issue names, is not in the repository. It exists only as uncommitted work in the main checkout, so this change cannot update it (section 9).

## Proposed Changes

### 1. `ConfigExportCommand`

New file `Cli/src/Commands/Configuration/ConfigExportCommand.cs`:

```csharp
[Command(CommandNames.EXPORT, Description = "Export the resolved configuration into a CI job: print ::add-mask:: for its values, then append them to GITHUB_ENV, write them to a file, or both.")]
public class ConfigExportCommand : BaseContextCommand
{
    public ConfigExportCommand(IExtendedConsole console, ICommandContext context, IConfigurationsApiClient configurationApi, IFileSystem fileSystem, GitHubActionsSettings gitHubActions);

    [Argument(0)] public string AnnotationKey { get; set; }
    [Option("--github-env", NoValue)] public bool IsGitHubEnvRequested { get; set; }
    [Option("--mask", SingleValue)] public string? Mask { get; set; }
    [Option("--prefix", SingleValue)] public string? Prefix { get; set; }
    [Option("--allow-unprefixed", NoValue)] public bool IsUnprefixedAllowed { get; set; }
    [Option("--allow-unresolved", NoValue)] public bool IsUnresolvedAllowed { get; set; }
    [Option("-e|--export", SingleValue)] public string? ExportFilePath { get; set; }
    [Option("-f|--format", SingleValue)] public string? Format { get; set; }
}
```

It is registered in the `[Subcommand(...)]` list of `ConfigGetCommand`, after `ConfigCompositionCommand`. It inherits `-p|--projectKey` and `-v|--view` from `BaseContextCommand`. It has no `-r`, `--version`, `--content-only` or `--env-prefix`: they parse as unrecognized options (exit `-1`).

| Option | Meaning |
| --- | --- |
| `--github-env` | Append every value to `GITHUB_ENV` as a variable with its `env` name. Needs `--prefix` or `--allow-unprefixed`. Refused when `GITHUB_ENV` is not set |
| `--mask bound\|all` | `bound` by default: mask the values that bindings produced, found by comparing with the effective document. `all` masks every string and number. `--mask` without `--github-env` and `--export` only prints the masks |
| `--prefix <PREFIX>` | `--env-prefix` of `story config`, renamed. Prepended to every variable name of `--github-env`, and to every name of the `--export` file in the `env` format |
| `--allow-unprefixed` | Let `--github-env` write the names of the configuration without a prefix |
| `--allow-unresolved` | Export binding expressions that were not resolved as they are, instead of refusing |
| `-e\|--export <file>` | Also write the content to this file. Always the content alone, replaced without asking, mode `0600` on Linux and macOS |
| `-f\|--format` | The format of the `--export` file: `env` by default, `flat`, `properties`, `yaml`, or `json` for the content alone. Needs `--export` |

**The default format of the file is `env`,** as the issue says and as the action's `format` input already defaults. Under `story config`, `--mask -e <file>` without `--format` wrote the JSON content. `-f json` keeps that.

**Validation.** Every check runs before the first call, and each message is one plain line on standard error. The order:

1. The key: "Please specify a valid annotation key; 'sform assist key' explains them."
2. The format: "Unknown format '{format}'; use env, flat, properties, yaml or json."
3. `--format` without `--export`: "--format is the format of the --export file: add --export <file>." (`--format` does nothing without a file. `story config` ignored it with masking.)
4. The mask: "Unknown mask '{mask}'; use bound or all."
5. No target: "Nothing to export: add --github-env, --export <file> or both, or --mask alone to print only the masks." The targets are `--github-env`, `--export`, and `--mask` on its own.
6. `--prefix` without `--github-env` and without an `env` file: "--prefix applies to --github-env and to --export in the env format only."
7. A prefix that is not an ASCII letter followed by ASCII letters, digits and `_`: "--prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_."
8. `--github-env` without `--prefix` and without `--allow-unprefixed`: "--github-env needs --prefix, for example APP_, or --allow-unprefixed: the names come from the configuration and reach every later step of the job."
9. `--allow-unprefixed` without `--github-env`: "--allow-unprefixed applies to --github-env only."
10. `--github-env` without `GITHUB_ENV`: "GITHUB_ENV is not set: --github-env runs in a step of a GitHub Actions job."

The refusals that only kept the two jobs apart are gone: `--resolved` and `--content-only` are implied, and `--version` does not exist. `--allow-unresolved` always applies.

**Execution.** This is `ExportAsync` of `story config`, moved without a change of behaviour:

- the output is always plain: UTF-8 on the console (`UseUtf8Output`), the masks on standard output, every message on standard error;
- one resolved read (`GetResolvedConfigurationConditionalAsync`). A failed read is reported as before: `404` "The configuration for '<key>' has not been found.", `422` the server's message and code, others `WriteApiErrorLine`, and `401`/`403` go to `Program`;
- a withheld-secrets response stops the command before anything is written, with the same message ("… Nothing was exported. …");
- then the computation, all before any write: the variables and their refusals ("Nothing was exported."), the payload of the file, one effective read, the unresolved bindings (refused unless `--allow-unresolved`), and the mask plan. The plan masks every string and number when the effective read fails (not `404`, which is wrong input) or returns another version or hash;
- then the writes in their order: the `::add-mask::` lines, `GITHUB_ENV`, the file;
- then the notes and the summary on standard error: the variables, the number of masked values and the mode, the names shorter than four characters that were not masked, and the file. Never a value.

### 2. `ConfigGetCommand` keeps the read job

- **Removed:** `--github-env`, `--mask`, `--allow-unprefixed`, `--allow-unresolved`, `IsMasking`, `ExportAsync`, `AppendToEnvironmentFile`, the `GitHubActionsSettings` dependency, the mask in the read options, and the validation that only applied to the export options. No hidden alias: the removed options parse as unrecognized options (exit `-1`).
- **Kept:** `-r|--resolved`, `--version`, `-f|--format`, `--content-only`, `-e|--export`, `--env-prefix`, the warning of withheld secrets, and the warning of unresolved bindings of a resolved read.
- `IsPlainOutput` is `--content-only`.
- `--env-prefix` applies to `--format env` only: "--env-prefix applies to --format env only."
- The descriptions of `-e` and `--env-prefix` no longer name `--github-env` and `--mask`.
- The constructor takes `IExtendedConsole`, `ICommandContext`, `IConfigurationsApiClient` and `IFileSystem`.

### 3. The shared helper `ConfigurationReads`

New file `Cli/src/Commands/Configuration/ConfigurationReads.cs`, an `internal static class` with what both commands need. They call it instead of keeping copies:

```csharp
internal static class ConfigurationReads
{
    public const string JsonFormat = "json";

    // The effective or the resolved read through the conditional twins, with SecretsWithheld.
    public static Task<(ConfigurationModel Configuration, string? SecretsWithheld)> ReadAsync(IConfigurationsApiClient api, ICommandContext context, string key, bool isResolved);
    public static Task<ConfigurationModel> ReadEffectiveAsync(IConfigurationsApiClient api, ICommandContext context, string key);

    // "json" and null are null, the JSON content; false for an unknown name.
    public static bool TryParseFormat(string? name, out ConfigurationFormat? format);
    public static bool IsValidPrefix(string prefix);

    public static JToken? ToToken(object? content);
    public static string ToContentJson(JToken? content);
    public static string Paths(IEnumerable<DocumentLeaf> values);

    // A plain line on the error output, or a line of the console.
    public static void WriteMessage(IExtendedConsole console, bool isPlainOutput, string message);
    public static int WriteReadFailure(IExtendedConsole console, bool isPlainOutput, string key, ApiException exception, int? version = null);
    public static bool TryFormat(IExtendedConsole console, ConfigurationModel configuration, ConfigurationFormat? format, string? envPrefix, out string payload);
    public static void WriteInvalidDocument(IExtendedConsole console, string target, InvalidDocumentException exception);

    // OwnerOnlyFile for resolved content on Unix, UTF-8 without a byte order mark otherwise.
    public static int WriteFile(IExtendedConsole console, bool isPlainOutput, IFileSystem fileSystem, string path, string text, bool isResolved);
}
```

The read of a version (`GetConfigurationVersionAsync`) and `WarnOfUnresolvedAsync` stay in `ConfigGetCommand`, the only command that uses them. `AppendToEnvironmentFile` moves to `ConfigExportCommand`. `MaskMode`, `MaskPlan` and `ConfigurationMasks` stay as they are.

### 4. Telemetry

`CommandPath` builds the path from the primary names, so a run reports `… config export`. Nothing changes.

### 5. The command tree and a key named `export`

McMaster matches a subcommand before the argument, so `sform story config export` always selects `config export`, as `set` or `versions` already do. An annotation key is never a single segment (`AnnotationKey.TryParse` needs a type code and at least two segments), so no key is lost. The docs say so in one sentence.

### 6. Tests

All in `Cli/test`.

- **`Commands/ConfigGetCommandTests.cs`** keeps the read job: the model, the formats, `--content-only`, the `--export` file, the read failures, the warnings, the workflow-command escaping of the read, and its `InvalidCommandLines` without the export rows. It gains:
  - `RemovedExportOptions_DoNotParse`: `--github-env`, `--mask all`, `--allow-unprefixed`, `--allow-unresolved` and `--prefix APP_` throw an `UnrecognizedCommandParsingException`;
  - the message of `--env-prefix` without `--format env`, also with `-r`.
- **`Commands/ConfigExportCommandTests.cs`** is new. It takes the export cases of `ConfigGetCommandTests`, without `-r` and `--content-only` and with `--prefix` for `--env-prefix`. They keep their expectations: the masks, the heredocs, the order of the writes (`FirstWriteProbe`), the refusals, the withheld secrets, the unresolved bindings, the version fallback, the failed and timed-out effective read, the `404` and `401` of the effective read, multi-line values, the properties file, a reserved name under a prefix, U+0000, the limits, and the workflow-command escaping. It adds:
  - `InvalidCommandLines` for every message of section 1, each without a call to the API and without a write;
  - the default format of the file (`-e app.env` is `env`, prefixed with `--prefix`), and `-f json` (the content alone);
  - `--mask` alone: the masks, no file, `GITHUB_ENV` untouched;
  - only the resolved and the effective read: never the version read;
  - the read failures of the resolved read on standard error: `404`, `422`, `500`, and `401` left to `Program`;
  - `Options_AreParsed`, and the options of `story config` that do not parse here (`-r`, `--resolved`, `--version 2`, `--content-only`, `--env-prefix APP_`).
- **`ProgramTests.cs`:** `config <key> --mask` now fails as an unrecognized option, which the existing theory still covers. New rows parse through `config export <key>`: `--mask` without a value, `--format env --format yaml` and `--bogus`. The missing-value test also covers `--prefix`.
- **The command tree.** A test parses `config export <key>` and `configuration export <key>` and gets a `ConfigExportCommand` with the key. `config export` without a key is wrong input with the key message. `StorytellerTransferCommandTests.Commands_AreSubcommandsOfStory_AndNoLongerOfConfig` checks types instead of names: `config` holds neither `StorytellerExportCommand` nor `StorytellerImportCommand`, no subcommand is named `import`, and its `export` is `ConfigExportCommand`.
- `ConfigCompositionCommandTests` keeps working. It may keep registering `GitHubActionsSettings`.

### 7. The GitHub Action

`GitHubAction/action.yml`:

- The header comment: "A thin wrapper of `sform story config export <key>`: …".
- **New input `allow-unresolved`** (default `'false'`): "true to export binding expressions that were not resolved as they are, instead of failing." It is checked as a boolean in **Check the inputs** and passed as `--allow-unresolved`. With the default, every existing combination of inputs behaves as before.
- **Check the inputs:** the `env-prefix` warning stays. Its rule, used by `export-env` or by `output-file` in the `env` format, is the rule of `--prefix`.
- **Read the configuration:**

  ```bash
  args=(story config export "$SA_KEY" "--mask=${SA_MASK:-bound}")
  # --github-env [--allow-unprefixed] with export-env, as before
  # "--prefix=$SA_ENV_PREFIX" under the same conditions as --env-prefix before
  # --allow-unresolved with allow-unresolved
  # "--export=$SA_OUTPUT_FILE" "--format=$format" with output-file
  ```

  `--resolved` and `--content-only` go: they are implied. The `--option=value` form stays for every value from an input, because McMaster expands an argument that starts with `@` as a response file. `--mask` moves to that form too: a `mask` input that starts with `@` could otherwise name a response file. A `mask` of `bound` or `all` behaves as before.

`GitHubAction/README.md`: the intro, the inputs table (`export-env` is `--github-env`, `env-prefix` is `--prefix`, the new `allow-unresolved`), step 1 and step 4 of **Steps**, the test class in **Checks**, and **Until sform is published**: the action of a commit runs `story config export`, so it needs a `sform` of that commit or newer. An older `sform` reads `export` as the key and ends with an unrecognized argument.

### 8. Documentation

- `docs/42for.net/cli/sform.md`:
  - The warning of "Sign in from a pipeline" names `sform story config export`.
  - "Read a configuration" keeps the read job: the synopsis without the export options, the output table without the masking row, the plain-output rules of `--format` and `--content-only`, and its exit codes.
  - "Export to GitHub Actions" (the heading stays for its links) describes `sform story config export`: the synopsis, the options, the targets, the default format, the ordered steps, its exit codes, and the sentence on a key named `export`. Its examples use `config export`.
- `docs/42for.net/cli/github-action.md`: the intro, the inputs (with `allow-unresolved`), "What the action does", and the unresolved bindings.
- `docs/Platform/Storyteller/authentication.md`, `configuration-formats.md` and `templating.md`: the commands and options become `sform story config export <key> --github-env --prefix APP_`.
- `docs/Platform/Storyteller/export-import.md`: the sentence on the removed view transfer says that `sform story config export` is now the export of a resolved configuration into a CI job.
- `CLAUDE.md`: the row of `GitHubAction/`.
- This spec, and a review in `docs/Platform/Cli/specs/reviews/` afterwards.

### 9. Out of scope

- **`docs/Platform/Cli.AutomationTests/test-cases.md`** is not in the repository. The review lists the cases that need the new command line (`CFG-GET-07`, `CFG-GHA-01` to `CFG-GHA-05`, `GEN-CLI-05`), for whoever commits that file.
- Other CI targets, such as a GitLab dotenv file or Azure DevOps `##vso[task.setvariable]`.
- The specs and reviews in `docs/**/specs/`, which are historical.
- A release of `sform`. The docs keep saying that these commands are newer than the `sform` on NuGet.

### Phases

1. The CLI: sections 1 to 6. One commit with the code and the tests.
2. The GitHub Action: section 7.
3. The documentation: section 8.

Then the review.
