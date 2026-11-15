# Phase 4 of Project Settings on the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Overview

Phase 4 delivers the DbCreator part of [Project Settings on the Server](../2026-11-15%20Project%20Settings%20on%20the%20Server.md) (spec section 8 and the phase-4 row of section 10). A data set may now carry `project-settings.json` at its root, in the file shape of `sform project settings -e`. DbCreator checks the file with the API's rules before anything is written. A seed then gives the set's settings to a project whose settings were never set, through the API's `IProjectSettingsService`, and never overwrites or merges settings that someone set. The outcome is part of the seed output, the seed result and the seed record, and `2sInitDb sets` lists the settings. The built-in sets `exhibit` and `showcase` carry settings, and `data-sets.md` describes the file and the seed step.

| Commit | Subject |
| --- | --- |
| `e7d405ef7` | feat(platform): seed project settings with data sets (424-266) |
| `24dcdd9c0` | docs(platform): describe the project settings of data sets (424-266) |
| `cad7a7725` | fix(platform): describe the project settings of an existing project in the seed's --project option (424-266) |

## What Was Done

### 8. DbCreator data sets

#### The file

As planned, with these details:

- `DbCreator/src/DataSets/DataSetProjectSettings.cs` (new):
  - `Read(fileSystem, folder)` returns null without the file. Otherwise it parses the file and reads it with `ProjectSettingsJson.Read`, so member names, type codes and values match in any case and `Format` is optional.
  - The file is parsed with comments and trailing commas allowed, as `set.json` is. A member given twice is refused (`AllowDuplicateProperties = false`).
  - A file larger than 64 KiB (`MaxFileBytes`) is refused before it is parsed. The API takes 16 KiB, so the limit is far above the largest valid file.
  - Every refusal is a `DataSetException` (exit `2`) whose message names the file and every problem by its JSON path (`$.Types.sbt.Color: …`).
- `DataSetFolder.GetEntries` leaves `project-settings.json` at the root out of the documents, so it is no ignored file. A `project-settings.json` below the root stays an ignored file. `ComputeHash` already covered every file.
- `DataSetContent` and `DataSetOverview` carry the settings (`ProjectSettings`, null without the file).

#### Validation

As planned, with these details:

- `DataSetSeeder.Plan` reads the settings of every set while it checks the limits, before the target is provisioned. An invalid file is wrong input (exit `2`).
- `DataSetProjectSettings.Agree` refuses the sets of one run whose settings differ (`ProjectSettingsRules.SameValues`, so the spelling does not count). The message names the first two sets that differ and their files. Equal settings are written once, by the first set, and the later sets find them `Unchanged`.
- The check covers every set of the run, its dependencies too, not only the named sets: they all seed into one project.
- `2sInitDb sets` reads the settings too, so a file that breaks a rule ends the listing as wrong input, as it ends a seed.

#### Seeding

As planned, with these details:

- `DataSetSeeder.SeedProjectSettingsAsync` runs after the provisioning and before the documents. It reads the project's settings through `IProjectSettingsService.GetAsync` and decides (`DecideProjectSettings`):
  - **Created**: the stored settings are at version 0 and differ from the file's. The seed writes them with `SetAsync`, the condition `ProjectSettingsCondition.Version(0)` and the caller `KeyValueCaller.System()`. They are stored at version 1, `Modified` by `System`, with a `ProjectSettingsChange` event in the project's audit trail.
  - **Unchanged**: the stored settings have the same values, at any version. Nothing is written, so the access point's ETag stays.
  - **Skipped** with `DocumentExists`: the stored settings have other values at version 1 or more, settings reset to the defaults included. The message is "The project has other settings (version N); a seed never overwrites them."
  - **Failed** with the error code of a write that fails (a `StorytellerException`). The set is not recorded and the run exits `4`; the documents are still seeded.
- A `ProjectSettingsPreconditionFailedException` (someone stored settings between the read and the write) is no failure: the seed reads the settings again and decides again, `Unchanged` or `Skipped`.
- A dry run decides and writes nothing. It decides from the defaults for a project that it would create (a `NotFoundException` is caught only in a dry run), and from the settings an earlier set of the run would create (`DryRunWrites`, which now holds the view overlay and the settings). So a later set with the same settings is `Unchanged`, as in the real run.
- `DataSetProvisioner` is unchanged in behaviour. The owner's role (Administrator or Owner of the project) is also the API's rule for a change of the settings.

#### Outcome, result, record and output

As planned, with these details:

- `DataSetProjectSettingsResult` (new) holds the set's settings, the outcome, the error code and the message. `DataSetSeedResult.ProjectSettings` carries it.
- `DataSetSeedItem.Kind` is now nullable. A listed outcome of the settings (`Skipped`, `Failed`) is an item with no kind and the path `project-settings.json` (`IsProjectSettings`).
- `DataSetSeedRecord.ProjectSettings` is the outcome (`Created`, `Unchanged`, `Skipped`). It is absent for a set without the file and in records written before this phase. The record's `Created`, `Unchanged` and `Skipped` count the settings with the documents and the annotations.
- `DataSetOutput`:
  - The seed prints `project settings: 7 types, time zone Europe/Oslo, date format dd.mm.yyyy: skipped DocumentExists` before the counts per kind, and the message of a skipped outcome below it. A failure is listed with the other failures. The totals count the settings.
  - A left-out time zone or date format is shown with its default, such as `time zone UTC (default)`.
  - `sets` prints `Project settings: 7 types, time zone America/Phoenix, date format yyyy-mm-dd` for a set that has them.
  - The line of an existing project now reads "The project '…' exists: its access is used as it is."
- The help of `seed --project` now reads "an existing one keeps its access, and its project settings once they are set". `ExitCodes` documents the new causes of `2` and `4`.

#### Built-in sets

As planned, with these details:

- `exhibit/project-settings.json` is rewritten into the file shape: the marker, PascalCase members, the seven types with their names, plurals, colors and icons, `America/Phoenix` and `yyyy-mm-dd`. The types are in the catalog's order (`rst`, `unt`, `sbt`, `usg`, `cnt`, `exe`, `uxe`), as `ProjectSettingsJson.ToJson` writes them, so the file equals an export.
- `exhibit/README.md` says that the seed gives these settings to a project that has none, and shows `sform project settings set -i … --replace` for the import route. A seed still counts `README.md` and `key-values.json` as ignored files until 424-265.
- `showcase/project-settings.json` comes from the admin UI phase. The stopgaps of that phase in both `BuiltInSetsTests` (the file counted as an ignored file and left out of the expected documents) are reverted.

#### Tests

As planned, with these details:

- `DbCreator/test`:
  - `DataSetProjectSettingsTests` (new): the file shape, member names and type codes in any case, the refusals (a color outside the catalog, a Windows time zone, an unknown date format, unknown members at the root and in a type, an unknown type code, another `Format`, no object), every problem listed, invalid JSON and a member given twice, the size limit, and `Agree`.
  - `DataSetSeederTests`: settings created after the provisioning and before the documents, never overwritten, decided again after a concurrent write, a failed write that fails the set but not its documents, equal settings of several sets written once, a dry run, refusals before anything is written, and `Plan` refusing sets that disagree. `FakeProjectSettingsService` (new) records every write with its condition and caller.
  - `SeedCommandTests` and `SetsCommandTests`: the output and the exit codes, including a failed write that exits `4`.
  - `DataSetFolderTests` (the root file left out, the hash), `DataSetOverviewTests`, and `BuiltInSetsTests` (`Exhibit_IsTheDemoOfAcmeCorpAndGlobexWithItsTypeNames`, the showcase's settings, and `ProjectSettings_AreInTheFileShape`, which compares each set's file with `ProjectSettingsJson.ToJson` in the file shape).
- `DbCreator/testint`:
  - `ProjectSettingsSeedTests` (new): a new project gets the settings with their audit event and the record's counts; a second run with `--again` finds them `Unchanged` and leaves the access point's ETag unchanged; a project with other settings keeps them; a dry run writes nothing; and a file that breaks a rule is wrong input before any write.
  - `BuiltInSetsTests`: `showcase` gets its settings, and the new `Exhibit_SeedsWithoutFailures` seeds the demo with its settings.

### 10. Documentation (phase-4 row)

As planned, with these details:

- `docs/Platform/Storyteller/data-sets.md`:
  - a `### project-settings.json` section: the file shape, the rules, the refusals with an example, one set of settings per run, the file at the root only, and never overwritten;
  - step 3 of "Seeding a set", with the table of outcomes and the second decision after a concurrent write;
  - the dry run, the record member, the seed and `sets` output with an example, the exit codes, step 4 of the provisioning, the code map, and the test table;
  - `exhibit` among the built-in sets; the sentence that the seed ignores the showcase's settings is gone.
- The `--project` row of the seed options still said that an existing project is used as it is. It is fixed in `cad7a7725` (see Reviews).
- The DbCreator line of `CLAUDE.md` mentions `project-settings.json` and the `exhibit` set.

### Test results

Run again for this review:

| Project | Result |
| --- | --- |
| `DbCreator/test` | 166 passed |
| `DbCreator/testint --filter-class "*ProjectSettingsSeedTests"` (vNext container) | 5 passed |
| `DbCreator/testint --filter-class "*BuiltInSetsTests"` (vNext container) | 5 passed |
| `DbCreator/testint --filter-class "*.SeedTests"` and `"*BulkSeedTests"` (vNext container) | 8 and 1 passed |

### Reviews

The review of phases 4 and 5 confirmed two findings, both low and both documentation:

1. **The reviews of phases 4 and 5 were missing.** This review and [Phase 5](2026-11-15%20Phase%205%20of%20Project%20Settings%20on%20the%20Server.md) were added.
2. **The `--project` row of the seed options in `data-sets.md` still said that an existing project is used as it is.** A seed writes the set's settings into an existing project whose settings were never set. The row now matches the command's help: an existing project is used when the owner is its Administrator or Owner, keeps its access, and gets the set's settings only when its settings were never set (`cad7a7725`).

### Deviations from the spec

1. **The agreement check covers every set of the run.** The spec refuses two named sets with different settings. Dependencies also seed into the one project, so their settings take part as well.
2. **`Failed` is an outcome.** The spec lists `Created`, `Unchanged`, `Skipped` and the dry run. A write that fails (other than the `412`) is `Failed` with its error code. The set is not recorded and the run exits `4`, as for a document.
3. **A `412` leads to a second decision.** When settings are stored between the read and the write, the seed reads them again and decides `Unchanged` or `Skipped` instead of failing.
4. **The seed record has a member `ProjectSettings`**, the outcome. Its counts include the settings.
5. **The item is not a new `ViewDocumentKind`.** The spec calls the settings "an item of its own kind". `ViewDocumentKind` belongs to the API's view import, so `DataSetSeedItem.Kind` became nullable instead, and the settings item is known by its path.
6. **The `sets` line reads `Project settings: N types, time zone …, date format …`**, with left-out values marked `(default)`. The seed's line starts with a lowercase `project settings:` like the other lines of a set.
7. **A 64 KiB file limit and the refusal of a member given twice** are additions. Comments and trailing commas are allowed, as in `set.json`.
8. **"Unchanged on a second run" is tested with `--again`.** A second run without it skips the set by its record and reads nothing.
9. **The run output and the help changed.** "exists and is used as it is" became "exists: its access is used as it is", and the help of `--project` names the settings.

### Left for later

- The `Data.cs` base seed still drops the settings of `42dotnet` and `42dotnet.main` (out of scope, spec section 11).
- The merge with 424-265 (key/value pairs of data sets) keeps both root files out of the documents and both seed steps, settings first (spec, merge notes).
