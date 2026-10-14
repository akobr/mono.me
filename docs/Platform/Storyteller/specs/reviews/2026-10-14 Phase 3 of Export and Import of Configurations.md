# Phase 3 of Export and Import of Configurations

## Overview

Phase 3 covers the CLI (section 3) of [Export and Import of Configurations](../2026-10-14%20Export%20and%20Import%20of%20Configurations.md), Linear 424-15. `sform story config export` starts an export of the context's view, waits for it, and saves the zip or unpacks it. `sform story config import` uploads a zip or a packed folder, waits for the import and prints the outcome of every file. Both use the regenerated NSwag client of Phase 2.

## What Was Done

### Commands

- `ConfigExportCommand` (`export`) and `ConfigImportCommand` (`import`) are subcommands of `ConfigGetCommand`; `CommandNames` has `EXPORT` and `IMPORT`.
- **Export**, as specified:
  - `-f|--format` takes `folder` or `git` without case; anything else exits with `ERROR_WRONG_INPUT`.
  - The zip is saved under the server's `FileName` in the current folder, in an existing folder (or a path that ends with a separator) given by `-o`, or as the file `-o` names. Missing folders are created.
  - `-x|--extract` unpacks into `-o` or into the file name without `.zip`. The folder must be missing or empty.
  - The last line reports the counts, the size and the destination. A failed job prints its error and exits with `ERROR_CRASH`; a `409 TransferLimit` prints the server's message and exits with `ERROR_WRONG_INPUT`.
- **Import**, as specified:
  - A folder is packed with every `*.json` below it, without `.git`, in ordinal path order; a folder without such files is refused. The upload's `fileName` is the folder's name with `.zip`, or the zip's own name.
  - `400`, `409` and `413` print the server's message (`GetErrorMessage`) and exit with `ERROR_WRONG_INPUT`.
  - The result is a header, one line of counts (`Would create …` and `would update …` for a dry run), a table of the items (outcome, key, version, message), a note when the list was truncated, and `Nothing was written (dry run).`. The exit code is `ERROR_WRONG_INPUT` when a file failed, otherwise `SUCCESS`.
- `--timeout` (minutes, default 10) stops waiting with `WARNING_ABORTED` and prints the job id; the job continues on the server.

### Services and output

- `Services/TransferPolling.cs` polls after 1, 1, 1, 2, 2 and then every 3 seconds. Its delay is a virtual method, so the tests wait for nothing. It is registered in `Startup`.
- `Services/ConfigurationArchives.cs`:
  - `PackFolder` builds the zip in memory through `IFileSystem`.
  - `ExtractAsync` buffers the download (a zip is read from its central directory at the end) and writes through `IFileSystem`. An entry that resolves outside the folder is refused.
  - **Deviation:** the extracted files keep the time of extraction. A zip time has no time zone, so setting it would shift files by the local offset.
- `Output/TransferConsoleExtensions.cs` writes the import result and formats sizes and counts.

### Tests

| Project | Result |
| --- | --- |
| `Cli/test` | 122 pass. New: `ConfigurationTransferCommandTests` (12): waiting and saving under the server's name, output to a folder or a file, `--extract` with `.git`, a non-empty target folder, a failed export, the timeout, an unknown format, packing a folder without `.git` and non-JSON files with the dry-run output, a zip import with a failed item and its table, a `400` from the server, a missing path, and an archive entry outside the folder. |

- `TestableIO.System.IO.Abstractions.TestingHelpers` (22.1.0, the version of the other TestableIO packages) is new in `Directory.Packages.props`, for `MockFileSystem` in `Cli/test`.
- **End to end:** the CLI needs a signed-in user, so it was not run against the local host. Instead, a scratch program called the regenerated `ConfigurationsApiClient` (the client the commands use) against the Debug host with Azurite and the Cosmos emulator. It ran a folder export to `Succeeded` and downloaded the `FileResponse` (5 entries). It imported that archive back as a dry run: `Succeeded`, 2 unchanged, 3 ignored files, no items. A missing export answered `ApiException<ErrorResponse>` with `404 NotFound`.

### Documentation

`docs/Platform/Storyteller/configuration-transfer.md` has a CLI section.
