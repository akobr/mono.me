# Phase 3 of the Export and Import of a View

## Overview

Phase 3 covers the CLI (section "Phase 3 — CLI") of [Export and Import of a View](../2026-10-15%20Export%20and%20Import%20of%20a%20View.md), Linear 424-40. The `sform story config export` and `sform story config import` commands of PR #88 become `sform story export` and `sform story import`, because an archive now holds the whole view. Both take `-i|--include` with the four kinds, and the import also takes `-e|--existing overwrite|skip`. The values are checked locally, before anything is read or sent. Packing a folder skips hidden paths and `__MACOSX`, by the server's rule. The import prints a five-column table of the documents, with each warning as a `!` row. Then come a summary table per kind with the total, and a line with the excluded and ignored files. A failed file or a rejected upload exits with `2`; warnings don't change the exit code. Only `src/Platform/Cli/**` changed. The API and the SDKs (Phase 2) and the admin UI (Phase 4) are not part of this phase. The code and the tests are in `96ca48f1`. The documentation is in `989a798b`, which also documents Phase 4. The review found no defect in the code.

## What Was Done

### 1. Commands (`Cli/src/Commands/Storyteller/StorytellerExportCommand.cs`, `StorytellerImportCommand.cs`)

- `Commands/Configuration/ConfigExportCommand.cs` and `ConfigImportCommand.cs` moved to `Commands/Storyteller/` as `StorytellerExportCommand` and `StorytellerImportCommand`. Git shows them as renames.
  - `StorytellerListCommand` (`sform story`) lists them as subcommands, after `StorytellerHistoryCommand`, and `ConfigGetCommand` no longer does.
  - `CommandNames.EXPORT` and `IMPORT` are unchanged.
  - `sform story config export` and `import` are removed without an alias, because they were never released (decision 13).
- The usage is the spec's:

  ```
  sform story export [-f|--format folder|git] [-i|--include <kinds>] [-o|--output <path>] [-x|--extract] [--timeout <minutes>]
  sform story import <zip-or-folder> [-i|--include <kinds>] [-e|--existing overwrite|skip] [--dry-run] [--force] [--timeout <minutes>]
  ```

  **Addition:** the options have value names (`<format>`, `<kinds>`, `<path>`, `<mode>`, `<minutes>`), and the argument is named `zip-or-folder`, so `--help` prints this usage. Some descriptions now mention the four kinds and the hidden paths: those of both commands, `--dry-run`, `--force` and the argument.
- **Export:**
  - It always sends a `ViewExportCreate` with the format and the include, as the Phase 2 review asked. For a missing body, the C# SDK would send `null`, and the API answers that with `400`. Without `--include`, `Include` is sent as null.
  - The zip is handled as in PR #88:
    - by default, it is saved under the server's file name in the current folder;
    - if `-o` names a folder, it is saved there under that name;
    - any other `-o` is the file itself;
    - `-x` unpacks it into a missing or empty folder.
  - **Addition:** the progress line names the kinds and the format: `Exporting the annotations and templates of acme.billing.default (git)…`.
  - The last line comes from `TransferConsoleExtensions.FormatExportSummary`. It gives the counts of the exported kinds, then the versions and the size, for example `Exported 4 annotations and 2 templates (9 versions, 2 KB) to …`.
    - A kind left out of the export has a null count and is not named.
    - The versions are not mentioned when only annotations were exported, since annotations have none.
- **Import:**
  - It passes the parsed include and existing to `StartViewImportAsync`. Before, both were null. `force` and `dryRun` are passed only when they are given, as before.
  - **Addition:** the progress line lists the options that differ from the defaults: `Importing acme.zip into acme.billing.default (annotations and templates, skip existing, forced, dry run)…`. Before, it only named a dry run.
- **Rejected starts.** Each command prints the server's message (`GetErrorMessage`). Section 5 has the exit codes.
  - The export catches `400`, `401`, `403` and `409`. Before, it caught only `409`.
  - The import catches `400`, `401`, `403`, `409` and `413`. Before, it caught `400`, `409` and `413`.
- `TransferPolling` is unchanged, except that its doc comment now speaks of a view.

### 2. `--include` and `--existing` (`Cli/src/Commands/Storyteller/ViewTransferArguments.cs`)

- This new static class checks the options of both commands before anything is read from disk or sent. It has:
  - `TryParseFormat` (the format check moved here from the export command), `TryParseInclude` and `TryParseExisting`;
  - `FormatKinds`, which words a list of kinds ("annotations, schemas, templates and configurations"), and `AllKinds`.
- Names match in any case. Only defined names are accepted (`TryParseName`), as in the API's own check. `Enum.TryParse` would also accept a number or comma-joined names.
- `--include` takes `annotations`, `schemas`, `templates` and `configurations`. Without it, the server takes all four.
  - **Deviation:** the option takes a comma-separated list, and it may also be repeated (`-i annotations -i templates`, `CommandOptionType.MultipleValue`). The API accepts both forms.
  - Entries are trimmed, and empty entries are dropped. A kind named twice counts once. The kinds are sent in import order, whatever their order on the command line (a `SortedSet<ViewArchiveContent>`).
  - **Deviation:** an `--include` that names no kind (`-i ""`, `-i ","`) is refused, although the API takes an empty include as all four kinds. On a command line, an empty value is more likely an empty shell variable than a request for everything. To include all four kinds, leave the option out.
- `--existing` takes `overwrite` or `skip`, trimmed and in any case. Without it, the server takes `Overwrite`.
  - **Deviation:** an empty `--existing ""` is refused for the same reason, although the API takes an empty `existing` as `Overwrite`.
- An unknown value exits with `2` and an error that names the value and the allowed ones. For example: `Unknown kind 'secrets' in --include; use annotations, schemas, templates or configurations, comma-separated.` The import checks its options before it looks at the path.

### 3. Packing a folder (`Cli/src/Services/ViewArchives.cs`)

- `Services/ConfigurationArchives.cs` is renamed to `ViewArchives.cs` (class `ViewArchives`).
- `IsSkippedPath` is new and public. It copies the server's `ViewArchiveNames.IsSkippedPath` from `Backend.Core`, which the CLI does not reference. A path is skipped when:
  - any segment starts with `.` (`.git`, `.github`, `.vscode`, `.DS_Store`, `._*`). A bare `.` segment does not count, as refined in Phase 1;
  - its first segment is `__MACOSX`, in any case;
  - the path is empty.
- `PackFolder` takes every `*.json` file below the folder, without the skipped paths. The extension matches in any case, as the server reads it. Before, it skipped only `.git` folders. This resolves the CLI note of the Phase 1 and Phase 2 reviews.
- **Refinement:** `PackFolder` walks the folder itself (`EnumerateDocuments`) and never enters a skipped folder, so a large `.git` is not walked. Before, it listed every file with `EnumerateFiles(…, AllDirectories)` and then filtered them.
- Unchanged:
  - entries are named by their path relative to the folder, with `/`, in ordinal order;
  - the upload is named `<folder>.zip`;
  - `ExtractAsync` refuses entries that would land outside the folder.
- A folder without such files is refused with `The folder '…' has no *.json files outside hidden folders.` and exit code `2`.

### 4. The import table (`Cli/src/Output/TransferConsoleExtensions.cs`)

`WriteImport` is rewritten. Under the header `Import of <file>`, it writes:

1. a table of the documents the job lists (every outcome except `Unchanged`), when there are any;
2. `Only the first N files are listed; the counts include all.`, when the job cut the list;
3. the summary table;
4. `N files excluded, M other files ignored.`, from the total's `ExcludedFiles` and `IgnoredFiles`;
5. `Nothing was written (dry run).`, after a dry run.

**The document table** (`GetItemRows`):
- It has the spec's columns **Kind**, **Document**, **Outcome**, **Code** and **Message**.
- `FormatKind` gives readable kind names: `Annotation`, `Type schema`, `Descendant-type schema`, `Annotation schema`, `Template` and `Configuration`.
- `FormatDocument` fills the Document column:
  - the key of an annotation, a configuration or an annotation schema;
  - the type code of a template or a type schema;
  - `exe below sbt.northwind` for a descendant-type schema;
  - the path, when the item names neither a key nor a type.
- **Code** is the item's `ErrorCode`. The old table did not show it.
- **Deviation:** the `Version` column of the PR #88 table is gone, because the spec lists exactly these five columns. The CLI no longer shows versions.
- **Warnings:** each warning is a row of its own under its document. The row has an empty Kind and Document, `!` (`WarningMark`) in the Outcome column, and the warning's code and message. The spec only says "marked `!`"; this keeps warnings aligned with the columns.

**The summary.**
- **Deviation (refinement):** the per-kind summary is a second table (`GetSummaryRows`, `GetSummaryHeaders`), not lines.
  - It has one row per entry of `Summaries`, in the server's import order, then a `Total` row.
  - Its columns are `Kind`, `Created`, `Updated`, `Unchanged`, `Skipped`, `Failed` and `Warnings`. After a dry run, `Created` and `Updated` read `Would create` and `Would update`.
  - It comes after the document table, so it stays visible below a long list. Before, the line of counts came first.
- `FormatContent` names the kind of a summary by `ViewImportSummaryContent`, the SDK's inline enum of `ViewImportSummary.Content`, as the Phase 2 review noted. A null content is the total.

### 5. Exit codes

| Code | Constant | When |
| --- | --- | --- |
| `0` | `SUCCESS` | The job succeeded and no file failed. Warnings don't change it. |
| `1` | `ERROR_CRASH` | The job failed on the server. Its error is printed. |
| `2` | `ERROR_WRONG_INPUT` | One of: a wrong option, path or folder to extract into; a start that the server refused with `400`, `409` or `413` (`InvalidArchive`, `TransferLimit`, `ArchiveTooLarge`); `Summary.Failed > 0`, also in a dry run. |
| `40` | `WARNING_ABORTED` | `--timeout` ran out. The job id and its status are printed, and for an import also the processed files. |
| `44` | `WARNING_UNAUTHORIZED_ACCESS` | The start answered `401` or `403`. |

- As specified, `2` covers a failed file and a rejected upload. Warnings don't change the exit code, and a comment in `StorytellerImportCommand` says so.
- **Deviation:** a `401` or `403` at the start prints the server's message and exits with `44`, not `2`.
  - The spec's "rejected the upload" is read as a rejected archive. Access errors keep the CLI's existing code, as in `KeyValueConsoleExtensions.WriteApiError` and `Program.cs`.
  - Before, the commands did not catch these exceptions. They reached the general handler of `Program.cs`, which prints "Total mayhem, the tool is broken!" and exits with `1`.
  - Since Phase 2 checks scopes per kind, a `403` that names `include` is an expected answer.
- The export now also catches `400`. The local checks should make it rare.

### Documentation

Commit `989a798b` documents Phases 3 and 4 together.
- `export-import.md`: the CLI section is rewritten. It covers:
  - the usage and the context options, and where the code is;
  - a table of the command options;
  - the checks of `ViewTransferArguments`, including the refused empty `--include`;
  - the steps of the export;
  - the steps of the import: packing, the progress line, both tables, the line of excluded and ignored files, and the dry-run note;
  - the exit codes `0`, `1`, `2`, `40` and `44`, and the tests.

  The subsections are titled "Command options", "sform story export", "sform story import" and "Exit codes", so their anchors don't clash with the existing `#options` and `#import`. The intro lists `src/Platform/Cli/src/`, and the "State of the delivery" paragraph is gone.
- `annotation-lifecycle.md`: its CLI section points to the two commands.
- The public pages `docs/42for.net/platform/configuration.md`, `annotations.md`, `using-the-platform.md` and `admin-ui.md` name `sform story export` and `sform story import`. All but `admin-ui.md` have examples.

## Tests

| Project | Result |
| --- | --- |
| `Cli/test` | 260 pass, 0 fail, 0 skipped (190 after Phase 2). `ConfigurationTransferCommandTests` (12 cases) is replaced, and 82 cases are new. These counts include each theory row. |

New test classes:
- **`StorytellerTransferCommandTests`** (24 methods, 34 cases):
  - **Setup:** the commands are registered under `sform story` and not under `sform story config`. Their options and defaults are parsed through `CommandLineApplication` with constructor injection.
  - **Export:** the body without and with include (the kinds in import order, and the counts of the included kinds only), and the output to a folder or a file. Also `--extract` and a folder that is not empty, a failed export and the timeout.
  - **Export rejections:** local ones for an unknown format, an unknown or numeric kind, and an `--include` without a kind. Server ones for `400`, `409` and `403`, with their exit codes.
  - **Import:**
    - a folder packed without hidden paths and sent with the defaults, and a folder of only hidden files;
    - the include, existing, force and dry run passed through;
    - an unknown `--existing` or `--include` refused before the path is read;
    - the tables with kinds, warnings and the summary per kind;
    - a failed file that exits with `2`, a failed import, the timeout and a missing path.
  - **Import rejections:** `400`, `409`, `413` and `403` from the server, with their exit codes.
- **`ViewTransferArgumentsTests`** (7 methods, 16 cases):
  - a missing include, and the kinds in any case, comma-separated or repeated, distinct and in import order;
  - unknown, numeric, `;`-joined and empty kinds;
  - `existing` in any case, and other values refused;
  - the formats, and the kinds in words.
- **`ViewArchivesTests`** (6 methods, 19 cases):
  - 14 `IsSkippedPath` cases;
  - packing without hidden folders and files, packing that keeps the content, and a folder of only hidden files;
  - unpacking: entries outside the folder, and the folders of the archive.
- **`TransferConsoleExtensionsTests`** (8 methods, 13 cases):
  - the kind and document of each document kind, and the path fallback;
  - the warning rows, the summary rows in the server's order, and the dry-run headers;
  - a dry run without items, the truncation note, and the export summary.

`RecordingConsole` records each `WriteTable` call as a `RecordedTable` (the header texts and the text of every cell) through a Moq `It.IsAnyType` setup. The tests therefore check the tables cell by cell.

Other checks:
- `dotnet build src/Platform/Cli/src` succeeds. The touched files add no warnings. `ConfigGetCommand.cs` keeps the CS8618, SA1508 and SA1518 warnings it already had, and the build also reports NU1510.
- `sform story export --help` and `sform story import --help` print the usage above. `sform story --help` lists both commands, and `sform story config --help` no longer does.
- **Not run end to end.** The commands need a signed-in user and a running host. Also, all output through `ExtendedConsole` crashes when stdout is redirected (see [Notes](#notes)), so the commands' messages could not be checked in a redirected run. The unit tests cover the output instead.

## Review

The review of this phase re-ran the build and the 260 tests. It found no defect in the code and changed nothing, so there is no review-fix commit. Its one finding was that this review document was missing, which this file resolves. No finding was dropped.

## Notes

- **Toolkit:** any output through `ExtendedConsole` crashes with "The handle is invalid" when stdout is redirected, from `Console.BufferWidth` in Alba's `ConsoleRenderer`. This bug predates this work and affects every `sform` command, including the new commands' error messages. It is not fixed.
- **Documentation:**
  - `docs/42for.net/cli/sform.md` is still a stub and lists no commands.
  - `export-import.md` does not say that an empty `--existing` is refused.
- **Not covered by a test:** a `401` at the start (the theories have only `403`), and a dry run with a failed file.
- **Still open from Phases 1 and 2** (backend, not touched by this phase):
  - concurrency exhaustion fails the whole job instead of one file;
  - descendant-type schemas that fail `CanDescend` can be written through the API and are exported, but their import fails with `InvalidKey`;
  - `ViewArchiveReader.Validate` counts every kind at upload.
