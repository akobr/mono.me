# Phase 3 of Project Settings on the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Overview

Phase 3 delivers the `sform` half of [Project Settings on the Server](../2026-11-15%20Project%20Settings%20on%20the%20Server.md) (spec section 7 and the phase-3 rows of section 10). It adds the commands `sform project settings`, `set`, `edit` and `reset`. It makes the console output for people follow the project's type names, colors, icons and date format, with a per-project cache and three ways to turn this off. It also adds eleven automation cases, `ACC-PRS-01` to `ACC-PRS-11`, and a "Project settings" section on the sform page. With no settings, no console, redirected output or the switches off, the output is byte-identical to the output before this phase.

| Commit | Subject |
| --- | --- |
| `d81c8de80` | feat(platform): read and change the project settings with sform project settings (424-266) |
| `8615f64ca` | feat(platform): show the project's type names, colors, icons and date format in the console output of sform (424-266) |
| `0c63871e6` | test(platform): automate the sform project settings cases (424-266) |
| `fb17a35a3` | docs(platform): describe the project settings in sform (424-266) |
| `0c4681748` | fix(platform): keep the project settings out of redirected output and show the icon symbols in sform project settings (424-266) |

## What Was Done

### 7.1 Commands

As planned, with these details:

- `src/Platform/Cli/src/Commands/Projects/`:
  - `ProjectCommand`: the help-only group `project` (`CommandNames.PROJECT`), registered in `PlatformCommand`.
  - `ProjectSettingsCommand` (`settings`): `-f|--format text|json` (any other value exits `2`) and `-e|--export`, which writes the file shape through the shared `WriteJsonToFile`.
  - `ProjectSettingsSetCommand`, `ProjectSettingsEditCommand`, `ProjectSettingsResetCommand`.
  - `ProjectSettingsCommands`: the versioned read, the diff and the outcome lines.
  - `ProjectSettingsDocuments`: conversions between the SDK and the model, the `--type` lookup, the `i-lucide-` prefix of `--icon`, `Pluralize` and `ETagOf`.
  - Every command is a `BaseContextCommand`, and `-v` is ignored.
- `Output/ProjectSettingsConsoleExtensions.cs`:
  - The table has the columns Code, Type, Name, Plural, Color and Icon. Values left to the built-in appearance or the defaults are dimmed.
  - A `■` swatch is drawn in the type's color only on a console whose output is not redirected and when `NO_COLOR` is unset.
  - Where icons are shown, the icon's symbol comes before its name (added by the review fix).
  - Problems print as `! <path>: <message>`.
  - API errors: `403` prints the API's message and exits `44`; `412` prints "The project settings changed meanwhile; they are at version N." and exits `2`; `400` prints every problem and exits `2`.
  - With `-f json`, a failure is one plain line on the error output.
- `set`:
  - The `-i` file is read with `ProjectSettingsJson.Read`.
  - `--dry-run` checks the change with `ReadMergePatch` (or `Read` with `--replace`) against the current settings and prints the diff.
  - A write without `--dry-run` leaves the rules to the server and prints its `400` problem by problem. This way an older sform with an older catalog does not refuse what the server accepts.
  - An empty value (`--label ""`, `--color ""`, `--time-zone ""`) removes the member.
  - `--clear` with other type options replaces the type's entry with only those values.
  - `--type` also takes `UnitOfExecution` and `unit-of-execution`.
  - A stale `--expected-version` is caught right after the read (exit `2`, no write), and `If-Match` is still sent on the write.
  - "Did not change" exits `42`, as `config set` and `account settings set` do.
- `edit`:
  - It exits `43` when the input or the output is redirected, and prints the `-e` / `set -i … --replace` route.
  - It checks the edited document with `ProjectSettingsJson.Read` and offers to re-open it. If the user declines, it keeps the edit in a file, prints the `set -i "<file>" --replace` command and exits `2`.
  - A `412`, a `403`, a `401` or a network error also keeps the edit.
  - The diff is shown before the confirmation.
- `reset` reads first and sends `If-Match` from that read. It exits `42` when the settings are already at the defaults, and shows the diff before it asks.
- `Services/Projects/IProjectSettingsCache` (`RememberAsync`) is called after every successful `set`, `edit` and `reset`. Its stand-in `NoProjectSettingsCache` was replaced by the provider of section 7.3.

### 7.2 Settings in other commands: output only, never input

As planned, with these details:

- `Services/Projects/ProjectDisplay` decides what the output shows: `PluralOf`, `NameOr`, `TreeTag`, `TypeCell`, `ColorOf`, `SymbolOf`, `FormatDate`, `FormatDateTime` and `FormatDay`. `Output/TypeNames` maps the API's type texts to the project's names.
- Places that follow the settings:
  - `story list` headers and tree tags (`StorytellerListCommand`);
  - the Type column and the dates of `story deleted`;
  - the composition sources (`CompositionConsoleExtensions`);
  - the `story import` table (`TransferConsoleExtensions.FormatDocument`);
  - `history` (`When`);
  - the `versions` tables of configurations, templates and schemas (`VersionsConsoleExtensions`);
  - `kv list`, `versions`, `recipients` and `audit` (`KeyValueFormats`, `AuditConsoleExtensions`);
  - the `kv usage` header (`FormatDay`);
  - the deleted notices of `story get` and `story delete`.
- Commands take `IProjectSettingsProvider` as an optional last constructor parameter. Null means `BuiltInProjectSettings.Instance`, so the existing test constructors are unchanged. Three test parse helpers register it for McMaster.
- A type the project does not name keeps today's text (`[context]`, `UnitOfExecution`, `template Unit`).
- Colors and icons appear only when the project changes at least one type. Then every type gets its own color and icon, or its built-in ones.
- A project that only sets a date format leaves the type output byte-identical. An explicit `yyyy-mm-dd` counts as the built-in format.
- Input parsing, files, machine-readable output, help and error texts are unchanged. Unit tests check that `--tree-root solution` is refused and that `story set` writes no time zone.

### 7.3 Loading (`IProjectSettingsProvider`)

As planned, with these details:

- `Services/Projects/ProjectSettingsProvider` is one singleton behind both `IProjectSettingsProvider` and `IProjectSettingsCache`. It keeps one display per project per run.
- It returns `ProjectDisplay.BuiltIn` without a request in these cases:
  - the standard output is not a console (`SformConsole.WritesToConsole`, from the review fix);
  - the output is switched off;
  - the context has no project.
- The cache:
  - The file is `project-settings.cache.json` in `CliPaths.StateDirectory` (`CliPaths.ProjectSettingsCacheFile`, `Constants.PROJECT_SETTINGS_CACHE_JSON`). Its records are `ProjectSettingsCacheFile`, `ProjectSettingsCacheEntry`, `ProjectSettingsCacheValues` and `ProjectSettingsCacheAppearance`.
  - Entries are keyed by base URL and `org.project`, with the `ETag` and the time of the read.
  - A read is fresh for 10 minutes. After that, `GetProjectSettingsConditionalAsync` sends `If-None-Match`, and a `304` keeps the entry. A read time in the future counts as stale.
- Additions not in the spec:
  - a 5 s read timeout before the built-in fallback;
  - at most 50 entries, newest first;
  - writes go to a temporary file that is then moved over the cache file.
- Every failure falls back to the built-in display with one debug log line.

### 7.4 Colors, icons and switches

As planned, with these details:

- `Output/TypeColors` maps the 18 catalog names to the admin UI's 500 shades and to the nearest `ConsoleColor`.
- `Output/TypeColorMode` has three modes: `None`, `Console` and `TrueColor`.
- `Output/TypeTagOutput` writes the 24-bit escape codes of the tree tags. It falls back to the console color, then to the lowlight color.
- `Output/TypeIconSymbols` maps the 65 icons to emoji of two UTF-16 code units each.
- `Output/TerminalCapabilities` reads `NO_COLOR`, `COLORTERM`, `WT_SESSION`, the terminal list of the spec, and UTF-8 output (code page 65001).
- 24-bit codes are written only to an output that is not redirected. On Windows they also need Spectre.Console's ANSI capability, whose check turns the escape-code mode on.
- `Configuration/OutputOptions` and `ConfigurationSections.OUTPUT` add the section. The preferences `output.icons` and `output.projectSettings` are in:
  - `UserSettingDefinitions`, `UserSettingValue` and `UserSettingsSchema`;
  - the help text and the `set`/`unset` descriptions of `account settings`.

  `account settings` names the environment variables as the source of a value.
- `Services/Projects/OutputSwitches` holds the switches, in this order:
  - `--builtin`, an inherited root option on `PlatformCommand` that `Program` records once parsing is complete;
  - `SFORM_PROJECT_SETTINGS` and `SFORM_ICONS`;
  - the `output` section.

  `ShowsIcons(encoding)` is the one decision about icons, used by the provider and by `ShowsTypeIcons`. `SformVariables` has `NO_COLOR`, `ICONS` and `PROJECT_SETTINGS`.

### 7.5 Tests

As planned, with these details:

- **Unit tests** (`src/Platform/Cli/test`):
  - `Commands/Projects/`: a fixture and five classes, covering every command path of the spec;
  - `Services/Projects/`: `ProjectSettingsProviderTests`, `ProjectDisplayTests`, `ProjectDisplayCommandsTests` and `OutputSwitchesTests`;
  - `Output/`: `TerminalCapabilitiesTests`, plus `TypeAppearanceTablesTests`, which checks every catalog icon and color against the tables;
  - `ColorRecordingConsole` records colors, and its `IsOutputRedirected` can be set;
  - updated: the account settings, composition, export and transfer tests.
- **Automation tests**:
  - Classes: `Cli.AutomationTests/src/Tests/Account/ProjectSettingsTests.cs` with the helper `ProjectSettingsOutput.cs`.
  - Testiny project `2SC`: folder 123 "Project Settings" under "Account and Access Points" (112), cases 499 to 509, one per `ACC-PRS-nn`.
  - What the cases cover:
    - a Reader's read as a table and as JSON;
    - set with `--clear` and the four audit events;
    - export, import and `--replace`;
    - edit with the fake editor, including the `412` from a second Administrator;
    - reset by parts, `-y`, the prompt and `43`;
    - `--dry-run` and invalid values;
    - a stale version together with the Reader, Contributor and machine refusals (`44`);
    - every color, icon and date format (65 runs);
    - the terminal against redirected output, `--builtin` and `SFORM_PROJECT_SETTINGS=builtin`;
    - the output preferences;
    - the context options.
  - Every write happens in a disposable project.
  - `coverage-values.json` has the values of `--format`, `--color` (18), `--icon` (65) and `--date-format` (5), and the two new keys of `account settings set` and `unset`. `--builtin` is in a `[Covers]` of ACC-PRS-09.
  - Testiny cases 460 (KV-AUD-05: twenty operations and a `ProjectSettingsChange` step), 476 and 477 (ACC-STG-03/04: the key list) were updated.
- **Self tests from the review fix**: the runner can redirect only the standard output of a pseudo-terminal run (`SformRunOptions.StandardOutputFile`). On ConPTY the file is the one handle the child inherits; with `script` it is a shell redirect. `SelfTests/ProjectSettingsTerminalSelfTests` runs two tests against `StubApi` without a target.

### 10. Documentation (phase 3 rows)

As planned, with these details:

- `docs/42for.net/cli/sform.md` has a new `## Project settings` section:
  - the four commands with examples and one exit-code table;
  - `### Where they show`, with what never changes and the redirected-output rule;
  - the color map, 24-bit colors, `NO_COLOR` and the icon detection;
  - `### Turn them off`;
  - `### The cache`.
- The same page also changed in these places:
  - the account settings part (`output.icons`, `output.projectSettings` and a typo fix);
  - the no-terminal, script-output, scope, files and variables tables.
- `docs/Platform/Cli.AutomationTests/test-cases.md`: the new sub-folder with its rows and bullets. Account has 37 cases (was 26) and the total is 269 (was 258). The KV-AUD-05 and ACC-STG-03 bullets were updated.
- `automation-tests.md`: the counts, a command tree of 115 commands and 1176 items, `StandardOutputFile` and the new self tests.
- The case count in the project README and in `CLAUDE.md` is now 269.
- `.github/workflows/Platform.Cli.UnitTests.yml` also triggers on the catalog file, which the new unit tests read, and `docs/github/workflows.md` says so.

### Test results

| Project | Result |
| --- | --- |
| `dotnet build src/Platform` | 0 errors, no new warnings |
| `Cli/test` | 2292 passed (run again for this review) |
| `Cli.AutomationTests` (no target) | 359: 90 passed (the coverage and catalogue checks, the self tests including the two new ones), 269 skipped (run again for this review) |
| `Cli.AutomationTests --treenode-filter "/**[Category=Coverage]"` | 9 passed |
| The eleven `ACC-PRS` cases against an API | not run: no API was reachable |

### Reviews

The reviewers reported eight findings. Four were confirmed: two defects, each reported twice. Both are fixed in `0c4681748`.

1. **Output redirected from a terminal still followed the settings** (confirmed, high and medium). `SformConsole.HasConsole` stays true while standard input or error is a console. So `sform story -t > tree.txt` or `| grep` from a terminal made a settings request, and wrote the project's names, the icons and, on Linux and macOS, raw 24-bit escape codes into the file or pipe.
   - The fix: the new `SformConsole.WritesToConsole` (`HasConsole && !Console.IsOutputRedirected`) now decides in `OutputTerminal.Of` and `ShowsTypeColors`, and `TakesEscapeCodes` also needs an output that is not redirected.
   - Unit tests: `OutputTerminal` with a redirected output; the provider returns `BuiltIn` with no call and no cache file; `ShowsTypeColors` with a redirected output.
   - With the old check put back, both new self tests failed: the file got `northwind [Company]` and `?? i-lucide-…`.
2. **The `project settings` table never showed the icon symbol** that spec 7.1 asks for (confirmed, medium and low).
   - `WriteProjectSettings` and `TypeCells` now take `showIcons`, so the Icon cell reads `🏢 i-lucide-building-2`.
   - `ShowsTypeIcons` needs a console whose output is not redirected.
   - `ProjectSettingsCommand` now gets `OutputSwitches` injected.
   - Unit tests cover the symbol on and off, the dimmed built-in icon, and always, never and auto against the terminal, UTF-8, redirected output and no console. The self test checks that neither symbols nor `■` reach a redirected file.

Refused:
- "A cache file with a null entry or `baseUrl` turns the display off for good": sform writes the file itself, so this does not happen in normal use.
- "`project settings -f json -e <file>` writes blank lines and a sentence after the JSON" (reported twice): this is the documented behaviour of the shared `WriteJsonToFile` of every `-e`.
- "`edit` keeps an older refused document after a later JSON syntax error": real, but minor (see Left for later).

### Deviations from the spec

1. The folder is `Services/Projects/`, not `Services/ProjectSettings/`. A namespace `…Services.ProjectSettings` would hide the `ProjectSettings` model type.
2. Section 7.2 relies on `SformConsole.HasConsole` being false for redirected output. That is not so when standard input or error is still a terminal, so the display uses `SformConsole.WritesToConsole`.
3. A type the project does not name keeps today's text, and colors and icons appear only when the project changes at least one type. Then every type gets its own or its built-in color and icon. A project with only a date format keeps the type output byte-identical.
4. `story export` prints one summary sentence and no result table, so only the `story import` table follows the settings.
5. The days of `kv usage` are dates, not instants, so they are not converted to local time. Without settings they keep today's culture-based text.
6. 24-bit colors on Windows also need Spectre.Console's ANSI capability, and are never written to redirected output.
7. `SFORM_PROJECT_SETTINGS` overrides `output.projectSettings` both ways (`project` turns the settings back on), as `SFORM_ICONS` does for `output.icons`. `--builtin` always wins.
8. `coverage-values.json` lists the two new preference keys but not their values. The second argument of `account settings set` is free text, and listing the values would force covering every `editor.type` value as well. ACC-PRS-10 exercises them.
9. The Reader, Contributor and machine refusals run in a disposable project with granted users and a `-w` machine, not in `Target:RoleProjects`: a Debug API skips role checks, so a refusal test there would really write settings. The stale `--expected-version` shares a case with the refusals, and `--dry-run` shares one with the invalid values.
10. The test that output redirected from a terminal shows no settings is a self test, not a step of ACC-PRS-09, so Testiny case 507 stays unchanged. The self tests need no target and run on every CLI pull request.

### Left for later

- Run the eleven `ACC-PRS` cases against a Release API. The terminal and icon checks are loose about spacing, and an icon may show as `?` in a Windows console.
- The `script` path of `StandardOutputFile` and the new self tests on Linux were not run locally. `Platform.Cli.UnitTests.yml` runs the self tests on Ubuntu; they skip where no pseudo terminal is available.
- Colors and icons were not checked by eye in real terminals. They are covered by unit tests and by the self tests in a pseudo terminal.
- `edit` keeps the last refused document, not a later edit that failed as JSON (a refused finding).
- The VitePress build of `docs/42for.net` was not run (no `node_modules`).
- Phase 4 (DbCreator data sets) and phase 5 (MCP and the remaining documentation).
