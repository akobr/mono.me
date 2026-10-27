# Phase 4 of Flat Output Formats for Configurations

Linear: [424-104](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github).

## Overview

Phase 4 covers section 7 of [Flat Output Formats for Configurations](../2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md), with the security notes of section 12 that concern `sform`, and the phase 4 docs of section 11. `sform story config` reads through the conditional twins, formats locally with the library of phase 1, so its text equals the API's `?format=`, and exports a resolved configuration into `GITHUB_ENV` with masks, a required prefix and a list of refused names. The review fixes of the phase and the final review of the branch closed gaps in what reaches the log of a job and the file system.

| Commit | Content |
|---|---|
| `73aee837` | The formats, `--content-only`, `--github-env`, `--mask`, the parsing exit code, the docs (section 7) |
| `581849f8` | Review fix: every line of a masked value is masked, `GITHUB_ENV` starts on a line of its own, a timeout of the effective read masks everything |
| `c91fbb1f` | Review fix: an empty formatted configuration is printed without Alba |
| `d8b8707c` | Review fix: every format of every read is exported in the tests |
| `d665130a` | Review fix: the docs say what `--export` prints and when the exit code is `2` |
| `8fa6124c` | Final review fix: every `#` of a message on standard error is escaped |
| `3700948a` | Final review fix: the export file of a resolved read is `0600` from its creation |
| `1196315b` | Final review fix: masks for the values as `properties`, `env` and `yaml` files write them |
| `5652d7d7` | Final review fix: the docs say that these options need a `sform` packed from the repository |

| Project | Passed (end of the branch) |
|---|---|
| `src/Platform/Cli/test` | 1055 |

## What Was Done

### 7. sform

**Options and reads.** `ConfigGetCommand` gained `-f|--format`, `--content-only`, `--env-prefix`, `--github-env`, `--mask bound|all`, `--allow-unprefixed` and `--allow-unresolved`, without a new subcommand. The effective and the resolved read go through `GetConfigurationConditionalAsync` / `GetResolvedConfigurationConditionalAsync`, which expose `SecretsWithheld`; `--version` keeps `GetConfigurationVersionAsync`. The content is formatted with `ConfigurationFormatter`, so a format also works for `--version`; tests compare the output with the formatter on the same content, a date and a decimal of the server included.

**Output.** As in the table of the spec: the model as before; a text format with the line-number gutter and without colours (`PlainOutputExtensions.WriteNumberedText`); `--content-only` the payload as it is, `{}` for the JSON of a configuration without content; `--github-env` and `--mask` only the `::add-mask::` commands. Raw output goes to `IExtendedConsole.Console.Out` with LF, `System.Console.OutputEncoding` is UTF-8 without BOM for the run (`UseUtf8Output`, set back afterwards, and only for the physical console), and every message of the raw modes is one plain line on `Console.Error` (`WriteErrorLine`, `WriteApiErrorLine`, `ToPlainLine`). Files are written through `IFileSystem`, UTF-8 without BOM and without a prompt. **Deviation (`c91fbb1f`):** a text format without `--content-only` on a configuration without values prints a blank line and "The configuration has no values." as raw text; the note went through Alba at first, which crashed in a run without a console. The output table of the spec and of `sform.md` does not list this case.

**Parsing errors.** `Program.RunAsync` maps every `CommandParsingException` (a missing option value, a repeated `--format`, `--version abc`) to `ERROR_INPUT_PARSING`, logs the command name without arguments or the message, and no longer reports it as a crash (`ProgramTests`).

**Validation.** Every row of the spec ends with exit code `2` and nothing written. **Addition:** an unknown `--mask` value ("Unknown mask 'x'; use bound or all."). A 422 of the API is handled before `WriteApiError` and is exit `2`; other API errors keep their exit codes.

**Withheld secrets and unresolved bindings.** A warning on standard error, and an error with exit `2` and nothing written under `--github-env` or `--mask`; an `@` string that equals the effective value at the same path is an unresolved binding, let through by `--allow-unresolved`. The effective document is read for the warning only when the resolved one holds an `@` string.

**`--github-env`.** `GitHubActionsSettings` reads `GITHUB_ENV` and supplies the delimiters (`ghadelimiter_` and 32 hex digits); `GitHubEnvironmentFile.FindRefusals` refuses U+0000, a variable longer than 32,767 characters, more than 512 KiB, and the denylist of the spec compared case-insensitively after the prefix; `Build` writes one heredoc per variable with a delimiter that no value contains. Everything is computed before the masks are written, the masks before anything else. **Review fixes (`581849f8`):** when a step of the job left the last line of `GITHUB_ENV` without its line feed, the runner would have read that line and the first `NAME<<DELIMITER` as one `NAME=value` and each line of the value as a variable of its own, past the prefix and the denylist, so `sform` now ends such a line first; a timeout of the effective read (the `TaskCanceledException` of the `HttpClient`) counts as a failed read and masks every string and number instead of crashing.

**Masking.** `ConfigurationMasks` and `MaskPlan` as in the spec: under `bound` a string or number whose path has no effective value or another one; every string and number when the versions or hashes differ or the effective read fails; no booleans or blank values; values under four characters are named, not masked, under `bound`. **Review fix (`581849f8`):** every non-blank line of a masked multi-line value is masked whatever its length; the four-character exemption is for whole values. **Final review fix (`1196315b`):** the runner also hides a few encodings of a mask, its JSON escape among them, which is how `flat` and the JSON content write a value, but `properties` writes every character outside printable ASCII as `\uXXXX`, `yaml` writes control characters with upper-case hex and `env` keeps them raw between double quotes. A later step that printed such a file showed a secret like `Pässwort-2026` as `P\u00E4sswort-2026`. `ConfigurationMasks.Plan` takes the format of the `--export` file and, for each masked value whose text in the file is neither the value nor its JSON escape, adds a mask for that text.

**Final review fix (`8fa6124c`): workflow commands in messages.** The runner reads the older `##[command]` syntax anywhere in a line, standard error included. Besides the paths of phase 1, `ToPlainLine` now writes every `#` of a message as `\u0023`, which also covers the messages of an API whose paths keep `#`; with no `#` left, no character that the runner's culture-sensitive search ignores can join two of them. `ConfigGetCommandTests` runs every message that names a path (an invalid document, a value too short to mask, the warning and the note of an unresolved binding, a 422 of the API) with a name holding `##[` and asserts that standard error holds no `#`.

**Final review fix (`3700948a`): the export file.** On Unix, a resolved read created or truncated the `--export` file with the default mode, then set `0600`, then wrote the content; a local user who opened the file in that window, or held a descriptor on an earlier world-readable file at the path, read the content once it was written. `OwnerOnlyFile.WriteAllText` writes into a new file next to the target, created with `FileMode.CreateNew` and the Unix create mode `0600`, and moves it over the target: the path never names a file with wider permissions while the content is written, a reader of an earlier file keeps that file, a symbolic link at the path is replaced instead of followed, and a failed write leaves no temporary file. `FileShare.Read` as in `File.WriteAllText`: with `FileShare.None`, .NET takes an exclusive advisory lock on Unix, and another .NET reader of the new file would have failed the write. `OwnerOnlyFileTests` run on Unix only, against the real file system. **Local check:** a scratch build of the same source ran in WSL Ubuntu 22.04 with umask `0022`: a watcher that polled the folder during 2,000 writes saw only mode `600`, an earlier `0644` file read through an open descriptor kept its old content, a directory at the path failed with `IOException` and left no file, and a symbolic link was replaced, not followed.

**Tests.** `ConfigGetCommandTests` (a strict `Mock<IConfigurationsApiClient>`, `MockFileSystem`, `StringWriter`s as `Out` and `Error`, parsing through `CommandLineApplication<ConfigGetCommand>`), `ConfigurationMasksTests`, `GitHubEnvironmentFileTests`, `PlainOutputExtensionsTests`, `OwnerOnlyFileTests` and `ProgramTests`. `d8b8707c` widened the export theory to `flat`, `properties`, `env` and `yaml` of the effective, the resolved and a version read, with and without `--content-only`, over a file that existed before.

### 11. Documentation (phase 4)

`docs/42for.net/cli/sform.md` gained "Read a configuration" with the options, the output table, standard output and error, UTF-8 and Windows PowerShell 5.1, the exit codes, "Export to GitHub Actions", the denylist, masking and its limits, what later steps see and the secret-delivery prerequisites; its warning about job logs was rewritten. `templating.md`, `key-values.md`, `authentication.md` and `configuration-formats.md` point to it. `d665130a` corrected the job-log warning (`--export` alone still prints the model), the scope of the UTF-8, prompt and `0600` rules, the exit codes, and what a machine with `Secrets.Read` but without mTLS gets. The final review fixes added: the runner's `##[` in printed lines, the replacement of the `0600` file, the masks of the text a file writes, and a warning at the start of "Read a configuration" and after the `--github-env` example of `authentication.md` that these options are newer than the `sform` on NuGet, with the link to the packing recipe.
