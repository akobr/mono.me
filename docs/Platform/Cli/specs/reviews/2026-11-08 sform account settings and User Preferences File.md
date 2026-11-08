# sform account settings and User Preferences File

Linear: [424-218](https://linear.app/424fun/issue/424-218/add-sform-account-settings-for-user-preferences-and-keep-them-editor).

## Overview

This review covers the single phase of [sform account settings and User Preferences File](../2026-11-08%20sform%20account%20settings%20and%20User%20Preferences%20File.md), sections 1-11. `sform` now keeps the editor and the default project and view in `sform.settings.json`, in the `.42for.net` folder of the user next to the stored session and the telemetry id. Before, they were written next to the assembly, in the install folder that `dotnet tool update` replaces. The configuration reads the file through a store, after `app.config.json`. The old `editor.config.json` and `access.default.json` are still read while the new file has no section of their own, and both move into it on the first change.

`sform account settings` lists the four preferences with the value the commands use and its source. `editor`, `set` and `unset` change them without a sign-in. An edit without a configured editor uses `$VISUAL`, then `$EDITOR`, before it asks.

The plan was reviewed before any code by three independent critics, against the code and the libraries. Their findings are in the plan as committed (`db25a726`): the lock, the lazy store at host build, no fallback to stale legacy values for a damaged file, PATHEXT, the console of a custom editor, the picker fallback for `$EDITOR`, the sources of `STORYTELLER_PROJECT`/`STORYTELLER_VIEW` and a machine sign-in, and the honest reach of the migration. After the implementation, a code review ran four lenses (store, commands, editor, tests). Every finding was then checked by a separate skeptic, and the 13 confirmed findings are fixed in `2617fd14` (see 12).

Commits:
- `db25a726` docs(platform): plan sform account settings and the user preferences file
- `82a38b17` feat(platform): keep the sform preferences in ~/.42for.net/sform.settings.json
- `1d347181` feat(platform): open the editor of $VISUAL or $EDITOR when sform has none
- `c495767f` feat(platform): add sform account settings to show and change the preferences
- `7cc59982` docs(platform): describe the sform settings and sform account settings
- `2617fd14` fix(platform): harden the sform settings found by the code review

Each feature commit builds and passes the CLI tests on its own (checked in a clean worktree): 1121, 1154 and 1210 tests. After the review fixes there are 1228, all passing, none skipped.

Deviations from the plan:
- The catalog (`UserSettingDefinitions`) came in with the store commit, because `account set` validates `-p` with its `TrySplitProjectKey`.
- The command-line splitting moved from `EditorService` to `Configuration/CustomEditorCommand`, so `editor.command`, `account settings editor -c` and `EnvironmentEditor` refuse a command of quotes alone with the same rule. `EditorService.SplitCustomCommand` delegates to it.
- The lock retries only for another holder: a sharing or lock violation (Windows 32/33), the EWOULDBLOCK of a flock (Linux 11, macOS 35), or an `UnauthorizedAccessException` while the lock file still exists (delete pending). Any other failure, such as a read-only or full disk, fails at once. The plan said "retrying for about five seconds" without these limits.
- A dangling link as the settings file reads as no settings, so the first change creates the file it points to.
- Leading comments are allowed (`CommentHandling.Ignore`); comments in and after the object already were.

## What Was Done

### 1. The user settings file

As planned.
- `Constants.USER_SETTINGS_JSON`. `ACCESS_DEFAULT_JSON` and `EDITOR_CONFIG_JSON` stay, commented as the legacy files.
- `Configuration/UserSettingsLocation`: `GetDirectory`, `GetFilePath` and `GetLegacyFilePaths` (`AppContext.BaseDirectory`, by section, case-insensitive). An unknown user root throws `InvalidOperationException` ("set HOME"). `InstallationIdStore`, `ProtectedTokenStore` and `EntraIdAuthenticationService` use `GetDirectory`.
- `Configuration/UserSettingsSchema`: the sections and properties (`SectionNames`, `GetProperties`), `Normalize`, `GetValue` (case-insensitive), `ParseText`, `ParseEditorType`, `ToEditorOptions`, `EditorChanges`, `AccessChanges` and `Apply`. `Apply` gives the normalised section that a set of changes leaves, for the no-op checks. `ParseEditorType` refuses a comma, so `"VsCode, Vim"` cannot parse as a flag combination of a defined value.

### 2. IUserSettingsStore

As planned, with `UserSettingsSnapshot`, `UserSettingsSection` (record), `UserSettingsLayer` and `UserSettingsException` (`IsDamaged`, `ExitCode`), one type per file.
- `Load` returns an empty object for a missing or blank file. It throws `UserSettingsException` for bad JSON, trailing text, a root that is not an object, or an owned section that is neither an object nor `null`. A `null` section counts as missing. Dates are not parsed (`DateParseHandling.None`).
- `Update` follows the order of the plan: folder, lock, load, migrate, change, write, delete the legacy files. The temporary file is `sform.settings.json.<guid>.tmp` and is deleted when the write fails. A settings file that is a link is written through: the store resolves the final target and replaces that file.
- `LockRetryDelay` is an internal init property (100 ms), so the lock tests run without waiting.

### 3. Precedence, loading and migration

As planned.
- `UserSettingsConfigurationSource` takes a `Func<IUserSettingsStore>`, so the store and its paths are created when the configuration loads. `Startup.ConfigureApplication` adds it after `app.config.json`, in place of the two `AddJsonFile` calls, before the user secrets. `WriteSettingsWarning` writes `! <message>` to standard error.
- `UserSettingsConfigurationProvider.Load` gives no data for a damaged file and for any other failure. The damaged file has its own message: "sform uses no editor and no default project from it until it is fixed (sform account settings --path)". Every other failure is reported as "The settings of sform cannot be read: …".
- The migration copies a legacy section only while the settings file lacks it. It deletes a legacy file only when that file holds nothing but its own section (`ReadLegacy` returns `HoldsOnlyItsSection`). A section emptied while its legacy file exists stays as `{}`. When the deletion then succeeds, a second write removes it.

### 4. The settings catalog

As planned: `UserSettingDefinition` with `DefaultValue` and `DefaultDisplay`, and `UserSettingDefinitions`.
- `access.project` defaults to `house.main`.
- `ReadProjectKey` fills a missing half as `CommandContext` does (`contoso.main`, `house.payments`). It returns null only when both halves are missing.
- `editor.command` takes only a command that names an executable (`CustomEditorCommand.IsValid`).
- `ParseEditorName` and `ToEditorName` map `vscode`, `neovim`, `vim` and `custom`.

### 5. Commands

As planned, in `Commands/Account/Settings/`. `CommandNames` has `SETTINGS`, `EDITOR` and `UNSET`, and `AccountCommand` lists `AccountSettingsCommand` before `AccountLogoutCommand`.
- **5.1** The table comes from `UserSettingValue.Resolve(snapshot, EnvironmentEditor, MachineSignInSettings)`:
  - The editor goes through `EditorResolution`. A custom type without a command shows `(ask on first use)` with the source `default (custom needs editor.command)`. A command without a custom type, or with no type, shows `settings (used by a custom editor only)`.
  - `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` show as their own source. An invalid `STORYTELLER_PROJECT` shows `(invalid, not <organization>.<project>)`, and with a valid one the view shows `default (STORYTELLER_PROJECT is set)`.
  - A machine sign-in shows `(of the machine sign-in)` with the source `machine sign-in (<credential variables>)`.
  - `--json` writes `EffectiveValue`, which is null for a value that is not used (`IsUnused`) and for a machine sign-in.
  - `--path` and `--json` go to the standard output in UTF-8 (`UseUtf8Output`), as `config get` writes its plain output. `--path` works with a damaged file.
- **5.2** `AccountSettingsEditorCommand` with `[Argument(0, "type")]` and `-c|--command` (property `EditorCommand`). The no-op compares the effective editor section with the normalised result of the change, and only when its layer is `Settings`, so a value that only a legacy file has is written and moved.
- **5.3** `AccountSettingsSetCommand`. The no-op is the same comparison through `UserSettingsSchema.Apply`. The hints cover `editor.command` without a custom type and `editor.type custom` without a command. `FindDefinition` is shared with `unset`.
- **5.4** `AccountSettingsUnsetCommand`. It also removes a value written by hand that `sform` cannot use: it checks the raw section as well as the normalised one. The message names the default (`sform uses house.main.`, `sform uses default.`) or, for `editor.type`, the next edit.
- The extended help text is a constant (`HELP_TEXT`), and `TheHelpText_NamesEveryKey` keeps it in step with the catalog.
- Exit codes come from `UserSettingsException.ExitCode`: `2` for a damaged file, `1` for one that cannot be read or written.

### 6. Default project writers

As planned. `AccountSetCommand`, `AccountRegisterCommand` and `AccessPointCreateCommand` inject `IUserSettingsStore` instead of `IFileSystem`. `AccountSetCommand.SaveDefaultProject` is the warning path of `register` and `access-point create`. `account set -p` without both parts stops before the account call with "'payments' is no project key; it takes <organization>.<project>." and `2`; before, it threw in the split.

### 7. Editor resolution and EditorService

As planned: `EnvironmentEditor`, `EditorResolution`, `ResolvedEditor` and `EditorOrigin`.
- `EditJsonAsync` resolves the editor once. An environment editor that cannot start is reported, then the picker runs, and the loop opens the chosen editor. A configured one ends with `ERROR_CRASH` and the hint. A custom choice without a command aborts with `WARNING_ABORTED`.
- `FindExecutable` lower-cases the PATHEXT extensions (`code.cmd`, as the files are named), trims quotes around folders of the PATH and skips invalid ones. `IsOnPath` (protected virtual) uses it, so the picker starts no `where`/`which` process. `IsCommandAvailable` is removed.
- `OpenFileInEditorAsync` is virtual. A blank executable (`""`) throws `Win32Exception`, so it is reported like an editor that is not installed. On Windows a custom executable is resolved with `FindExecutable`, and `CreateNoWindow` is set only for the VS Code `cmd.exe`.
- `SetupEditorPreference` prints `Saved in <path>; change it with sform account settings editor.` after a save.

### 8. Registration

As planned: `IUserSettingsStore` and `EnvironmentEditor` are singletons next to `IEditorService`.

### 9. Tests

As planned, in the classes the plan names, plus `Configuration/JsonAssertions.ShouldBeJson`, which compares JSON whatever the order and the formatting.
- `UserSettingsStoreTests` (38): the cases of 9, comments, a link to a missing file, a legacy file with more than its section, a failed write without a temporary file left, a held lock that is waited for, a lock that stays held (50 attempts), and a read-only lock that fails at once.
- `UserSettingsConfigurationTests` (17), `EditorResolutionTests` (11), `AccountSettingsCommandsTests` (64), `AccountDefaultProjectWritersTests` (11) and `EditorServiceTests` (25).
- Edit tests use a subclass of `EditorService` that records the editor and edits the file, so no process starts.
- The Unix PATH test compares with `Path.Combine`, so it runs on Windows too. The code review also ran the whole suite in the `mcr.microsoft.com/dotnet/sdk:10.0.401` Linux image: all tests passed.

### 10. Out of scope

As planned. `auth.discovery.json` stays next to the assembly; its comment now says so.

### 11. Documentation

As planned.
- `docs/42for.net/cli/sform.md` has a **Settings** section after **Telemetry**: the file and its folder, the table of keys, the commands, the sources, custom commands and `PATHEXT`, the legacy files with the one-time loss of a `dotnet tool update`, comments, and a damaged file. **Sign in** points to it from `sform account set`.
- `docs/Platform/Storyteller/authentication.md` (lines 263 and 408), and the comments of `CommandContext`, `CommandContextTests` and `AuthenticationDiscoveryCache`, are updated.

### 12. Code review

Four lenses reviewed the branch. Each finding was checked by a separate skeptic told to refute it, and all 13 were confirmed. `2617fd14` fixes them:
- A leading comment made the file damaged (`JToken.ReadFrom` returned the comment). It now loads.
- The lock retried every `IOException` for five seconds and then blamed another run, so a read-only file system stalled. A delete-pending lock file on Windows (`UnauthorizedAccessException`) failed at once. Both now behave as described in the deviations.
- A dangling link as the settings file failed every run with "cannot be read". It now reads as no settings.
- `""` as a custom command or as `$EDITOR` crashed `Process.Start` with `InvalidOperationException`. It is now refused, or reported as an editor that cannot start.
- The list and `--json` showed an incomplete editor as the editor in use. They also listed an organization without a project as `house.main` and ignored an invalid `STORYTELLER_PROJECT`. All three now follow the commands.
- `--path` and `--json` wrote in the console code page, so a non-ASCII profile path became `?`. They now write UTF-8.
- Five test gaps are closed:
  - `--json` with null values: a mutation that wrote the shown text instead of the value passed every test;
  - a failed write itself: the existing test failed before the write;
  - a legacy file with more than its section;
  - the exit code of a read failure;
  - the Unix PATH test, which was skipped on Windows.

### Manual check

`sform account settings --help`, `--path` and `--json` were run from the build. In the shell of the implementation there is no console buffer, so the table commands stop in the console renderer of the toolkit, which every rich-output command of `sform` does there. The other cases are covered by the tests. `MsalCacheHelper.UserRootDirectory` cannot be redirected on Windows (it uses the known folder, not `LOCALAPPDATA`), so that run wrote to the real `%LOCALAPPDATA%\.42for.net`. The test file it created was removed afterwards, and the folder was left as it was (`msal.cache`, `telemetry.json`).
