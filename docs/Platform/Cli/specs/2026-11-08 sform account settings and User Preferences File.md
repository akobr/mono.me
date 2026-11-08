# sform account settings and User Preferences File

Linear: [424-218](https://linear.app/424fun/issue/424-218/add-sform-account-settings-for-user-preferences-and-keep-them-editor).

Line numbers refer to the tree this plan was written against (`a6974e62`). No application code is changed by this document.

## Problem

`sform` opens an editor in `sform story config edit`, `config patch`, `template edit` and the schema edit commands. The first time, it asks which editor to use and saves the answer. After that, the choice cannot be changed or even seen: there is no command for it, and the user has to find `editor.config.json` and edit or delete it by hand.

The file sits in the wrong place. It is written next to the `sform` assembly, which for a dotnet global tool is the tool's install folder (`~/.dotnet/tools/.store/42.platform.cli/<version>/…`):

- `dotnet tool update` installs into a new versioned folder and deletes the old one, so the choice is lost and the user is asked again after every update;
- the install folder can be read-only (a shared tool path, a container image, a CI cache), and then saving the choice throws in the middle of an edit;
- two installed versions keep two different choices.

The stored session (`msal.cache`, the AuthKit token store) and the telemetry id already live in the user's `.42for.net` folder (`%LOCALAPPDATA%\.42for.net` on Windows, `~/.42for.net` elsewhere), which survives updates. Preferences belong there too.

`access.default.json`, the default project and view that `sform account set`, `sform account register` and `sform account access-point create` write, has the same problem: it is written next to the assembly too, so an update forgets the default project and every command asks for `-p` again.

The issue asks for all of it: a `sform account settings` command to view and change preferences, starting with the editor, moving both files to the user folder, and the `$VISUAL` / `$EDITOR` fallback that most CLIs have.

## Current State

### Editor options

- `Configuration/EditorOptions.cs` (lines 3-18): `EditorType` (`VsCode`, `Neovim`, `Vim`, `Custom`), `EditorOptions { EditorType?, CustomCommand?, IsConfigured }`. `IsConfigured` is false without a type, or for `Custom` without a command.
- `Startup.ConfigureApplication` (lines 71-88) adds `app.config.json` (required), `access.default.json` and `editor.config.json` (both optional) by relative path, so they resolve against the content root (`AppContext.BaseDirectory`, the generic host's default), then user secrets in development. A damaged optional file stops the host from building, so every command fails with "the application failed to initialize".
- `Startup.ConfigureOptions` (line 194) binds `EditorOptions` to the `editor` section (`ConfigurationSections.EDITOR`). An `EditorType` value that is not an enum name or number throws when the options are first read.
- The options are read once per run; the commands get them through `IOptions<EditorOptions>` (`ConfigEditCommand`, `ConfigPatchCommand`, `TemplateEditCommand`, `SchemaAnnotationEditCommand`, `SchemaDescendantEditCommand`, `SchemaTypeEditCommand`).

### EditorService

- `Services/EditorService.cs`, registered as a singleton (`Startup.cs`, line 60), takes only `IFileSystem`.
- `SetupEditorPreference` (lines 36-81) detects `code`, `nvim` and `vim` by starting `where`/`which` (up to three processes, five seconds each), lets the user pick one or type a custom command, and saves it with `SaveEditorOptions`.
- `EditJsonAsync` (lines 135-142) calls `SetupEditorPreference` when `options.IsConfigured` is false. There is no fallback to `$VISUAL` / `$EDITOR`.
- `OpenFileInEditorAsync` (lines 90-123) starts `cmd.exe /c code --wait` on Windows and `code --wait` elsewhere, `nvim`/`vim` with shell execute, or a custom command parsed by `ParseCustomCommand` (lines 338-351), which splits at the first space. A custom command starts with `UseShellExecute = false` and `CreateNoWindow = true`, so on Windows:
  - only an `.exe` is found by its bare name: `code --wait` (VS Code ships `code.cmd`) throws `Win32Exception` out of the command ("Total mayhem");
  - a terminal editor (`vim`, `nano`) runs in a hidden console, and the edit seems to hang.
- An executable path with spaces (`C:\Program Files\Notepad++\notepad++.exe -multiInst`) cannot be used.
- `SaveEditorOptions` (lines 358-367) serializes `new { editor = options }` with Newtonsoft into `<assembly directory>/editor.config.json`. The default settings write the enum as a number and include the read-only `IsConfigured`, e.g. `{"editor":{"EditorType":0,"CustomCommand":null,"IsConfigured":true}}`. The binder ignores `IsConfigured` and accepts the number, so the file loads.

### The default project

- `AccountSetCommand.CreateAccessDefaultConfigFile` (`AccountSetCommand.cs`, lines 88-98) writes `{"access":{"OrganizationName":…,"ProjectName":…,"ViewName":…}}` (PascalCase, nulls included) into `<assembly directory>/access.default.json`. It is also called by `AccountRegisterCommand` (line 91) and `AccessPointCreateCommand` (line 61). `account set -p <key>` with a key without a dot throws in the split (line 73).
- `AccessDefaultOptions` (`Configuration/AccessDefaultOptions.cs`) has `OrganizationName`, `ProjectName` and `ViewName` and is bound to the `access` section (`Startup.cs`, line 191). `AccountCommand`, `AccountSetCommand`, `CommandContext`, `AccessPointGetCommand`, `AccessPointGrantCommand` and `AccessPointRevokeCommand` read it (`StorytellerSetCommand` injects it without using it).
- `CommandContext` (lines 25-70) picks the target: `STORYTELLER_PROJECT` / `STORYTELLER_VIEW` first, also for a user; a machine sign-in never uses the stored project; a user without one gets `house.main`; the stored view belongs to the stored project only.

### The account command and the user folder

- `Commands/Account/AccountCommand.cs` (lines 22-29) lists the subcommands `AccountRegisterCommand`, `AccountSetCommand`, `AccessPointListCommand`, `MachineListCommand`, `SharedCertListCommand`, `CaDownloadCommand` and `AccountLogoutCommand`.
- `Telemetry/InstallationIdStore.cs` (line 21), `Authentication/ProtectedTokenStore.cs` (line 28) and `Authentication/EntraIdAuthenticationService.cs` (line 135) each build the folder as `Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net")`; the two stores take the directory through an internal constructor for the tests. `MsalCacheHelper.UserRootDirectory` is null on Linux or macOS without `HOME` and a user name; today only the telemetry, which catches everything, touches the folder at startup.
- `Startup.ConfigureServices` captures environment-driven settings once as singletons: `MachineSignInSettings.FromEnvironment(System.Environment.GetEnvironmentVariable)` and `GitHubActionsSettings.FromEnvironment(...)`.
- `Commands/CommandNames.cs` holds every command name (`ACCOUNT`, `SET`, …); telemetry reports command paths from the parser (`Telemetry/CommandPath.cs`), so new commands need no registration there.
- `auth.discovery.json` (`AuthenticationConfigurationResolver`, line 46) is a cache of the API's discovery answer next to the assembly; a cache may be lost, and it stays where it is.
- `[Command(ExtendedHelpText = …)]` takes a compile-time constant, and McMaster shows the help without constructing the command.
- Tests: `src/Platform/Cli/test` (xUnit v3, Shouldly, Moq, `MockFileSystem`, `RecordingConsole`); `IEditorService` is mocked in `SchemaOperationsTests` and `TemplateCommandsTests`. There are no tests of `EditorService`, `account set`, `account register` or `access-point create` yet.

## Proposed Changes

### 1. The user settings file

- New constant `Constants.USER_SETTINGS_JSON = "sform.settings.json"`; `ACCESS_DEFAULT_JSON` and `EDITOR_CONFIG_JSON` stay as the legacy names.
- New static `Configuration/UserSettingsLocation`:
  - `GetDirectory(IFileSystem)` = `Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net")`, the folder of the token store and the telemetry id; an unknown root throws `InvalidOperationException` ("set HOME"). `InstallationIdStore`, `ProtectedTokenStore` and `EntraIdAuthenticationService` switch to it, so the folder is defined once.
  - `GetFilePath(IFileSystem)` = `<directory>/sform.settings.json`.
  - `GetLegacyFilePaths(IFileSystem)` = `access` → `<AppContext.BaseDirectory>/access.default.json`, `editor` → `<AppContext.BaseDirectory>/editor.config.json`: where the configuration read them from until now.
- File shape: the sections the user owns, camelCase names, enums as their names:

  ```json
  {
    "access": {
      "organizationName": "contoso",
      "projectName": "payments",
      "viewName": "default"
    },
    "editor": {
      "editorType": "Custom",
      "customCommand": "code --wait --new-window"
    }
  }
  ```

- The owned sections and their known properties are listed once in `Configuration/UserSettingsSchema`: `access` (`organizationName`, `projectName`, `viewName`) and `editor` (`editorType`, `customCommand`). `UserSettingsSchema.Normalize(section, JObject)` returns a section with only the known properties, matched case-insensitively and named in camelCase, string values trimmed and non-empty, and `editorType` as an enum name (a name in any case or a defined number; anything else is dropped). Section names are matched case-insensitively too. It is used for the legacy files, for the configuration and for the list command, so all three read a value the same way.

### 2. IUserSettingsStore

New `Configuration/IUserSettingsStore` and `UserSettingsStore` (singleton, `IFileSystem`, internal constructor taking the file path and the legacy paths for tests):

```csharp
public interface IUserSettingsStore
{
    string FilePath { get; }

    // The user file as written, or an empty object when it does not exist (or is blank). A damaged file
    // (no JSON object, an owned section that is neither an object nor null) or an unreadable one throws
    // UserSettingsException with the path and the reason; it is never overwritten.
    JObject Load();

    // The normalised section of its legacy file, or null when the file is missing, damaged or has nothing of it.
    JObject? LoadLegacy(string section);

    // The effective owned sections, each with where it came from: the user file's section when the file has it
    // (an empty one too), else the legacy file's, else none.
    UserSettingsSnapshot Read();

    // Sets (value not null) or removes (null) properties of one section, keeps every other property and section,
    // migrates the legacy sections first (see 3) and writes the file.
    void Update(string section, IReadOnlyDictionary<string, JToken?> changes);
}
```

- `UserSettingsSnapshot` holds, per owned section, the normalised `JObject` and a `UserSettingsLayer` (`None`, `Settings`, `Legacy`).
- `UserSettingsException(message, isDamaged)`: a damaged file is wrong input (`ERROR_WRONG_INPUT`), a file that cannot be read or written is a failure (`ERROR_CRASH`); `ExitCode` gives the code, so every command maps it the same way.
- `Update`:
  - creates the folder and holds an exclusive handle on `sform.settings.json.lock` (`FileShare.None`, `FileOptions.DeleteOnClose`, as the MSAL cache of the folder does) from the load to the deletion of the legacy files, retrying for about five seconds, so two `sform` runs cannot write over each other's moved sections;
  - writes atomically into `sform.settings.json.<random>.tmp` in the same folder, then `File.Move(tmp, path, overwrite: true)`, so an interrupted write never leaves half a file; a settings file that is a link (dotfiles) stays a link: the file it points to is replaced;
  - replaces a property of the changed section whatever the case of its existing name, keeping its place.
- `IOException`, `UnauthorizedAccessException`, `NotSupportedException` and `SecurityException` become a `UserSettingsException` with the path.
- JSON through Newtonsoft (`JObject`), which the CLI already uses for editing and which keeps unknown properties untouched; dates are not parsed (a view that looks like a date stays text); comments in the file are not kept by a write.

### 3. Precedence, loading and migration

- **Precedence by section.** The user file's section, even an empty `{}`, replaces the legacy file's section as a whole. Migration (below) always copies a whole section, so this is the same as "the new file wins" for every file `sform` wrote, and it needs no `null` marker: `unset` of the last property leaves an empty section while the legacy file exists, which keeps an undeletable legacy file shadowed.
- **Loading.** `Startup.ConfigureApplication` drops the two relative `AddJsonFile` calls of the legacy files and adds a configuration source over the store at their position, after `app.config.json` and before the user secrets:

  ```csharp
  builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);
  builder.Add(new UserSettingsConfigurationSource(() => new UserSettingsStore(new FileSystem()), WriteSettingsWarning));
  ```

  `UserSettingsConfigurationProvider.Load` creates the store and flattens `store.Read()` into `editor:editorType`, `access:projectName`, …, so the configuration binds exactly what the list command shows. Only the owned sections and their known, valid properties are loaded: an invalid `editorType` written by hand is ignored instead of failing every edit command. Nothing here may stop `sform`, which also runs in pipelines that never use the settings:
  - a damaged user file is reported on standard error (`! The settings file <path> is damaged: <reason> sform uses no editor and no default project from it until it is fixed (sform account settings --path).`) and gives no settings: not the legacy files either, whose values the user file may have replaced long ago;
  - any other failure (a user folder that cannot be found) is reported the same way and gives no settings.
- **Write.** Every `Update` first migrates each owned section that the user file does not have yet and whose legacy file has it (normalised, see 1), then applies the change. So the first change of either preference moves both files.
- After the write, the store tries to delete each legacy file it migrated, but only a file that holds nothing but its own section (a file with more, written by hand, stays, and its section is shadowed). A failure (read-only install folder) is ignored. A damaged legacy file is not migrated and not deleted.
- A section emptied by the change is removed unless its legacy file still exists; then it stays as `{}`, and it is removed once the legacy file is gone.
- `sform` never writes `editor.config.json` or `access.default.json` again; `EditorService.SaveEditorOptions` and `AccountSetCommand.CreateAccessDefaultConfigFile` are removed.
- **Reach of the migration.** The legacy files are found where they sit next to the running assembly: a run from source, a tool in a fixed folder, the same install. `dotnet tool update` deletes the old version folder with the files in it, so the update that brings this change cannot recover them, and the user picks the editor and runs `sform account set` once more; from then on the preferences survive updates. Probing other version folders (the NuGet cache of a local tool) is out of scope.

### 4. The settings catalog

`Configuration/UserSettingDefinitions` keeps the CLI keys, the section and properties, the allowed values, the default and the description in one place:

| Key | In the file | Values | Default | Description |
|---|---|---|---|---|
| `editor.type` | `editor.editorType` | `vscode`, `neovim`, `vim`, `custom` (case-insensitive) | ask on first use | Editor of `config edit`, `config patch`, `template edit` and the schema edit commands |
| `editor.command` | `editor.customCommand` | any non-empty text | – | Command line of a `custom` editor; the file path is appended |
| `access.project` | `access.organizationName` + `access.projectName` | `<organization>.<project>`, both parts | `house.main` | Default project of the commands without `-p`; split at the first `.` as `sform account set` does |
| `access.view` | `access.viewName` | any non-empty text | `default` | Default view of the commands without `-v` |

```csharp
public sealed class UserSettingDefinition
{
    public required string Key { get; init; }
    public required string Section { get; init; }
    public required IReadOnlyList<string> Properties { get; init; }        // removed by unset
    public required string Description { get; init; }
    public required string ValueFormat { get; init; }                       // shown when a value is refused
    public IReadOnlyList<string>? AllowedValues { get; init; }             // null: free text
    public string? DefaultValue { get; init; }                             // what the commands use when not set
    public required string DefaultDisplay { get; init; }                   // what the list shows then
    public required Func<string, IReadOnlyDictionary<string, JToken?>?> Parse { get; init; }   // null: invalid
    public required Func<JObject, string?> Read { get; init; }             // the shown value, null: not set
}
```

`access.project` writes two properties; `unset access.project` removes both names and keeps the view. A stored project without an organization reads as `house.<project>`, as `CommandContext` uses it. `set` does not check the project against the API (it works offline and signed out); `sform account set` stays the checked, interactive way. New preferences are added by one entry here and one in `UserSettingsSchema`, plus a line of the help text (a constant; a test checks that it names every key); the `set`, `unset` and list commands need no change.

### 5. Commands

New command names in `CommandNames`: `SETTINGS = "settings"`, `EDITOR = "editor"`, `UNSET = "unset"` (`SET` exists). `AccountCommand` gets `typeof(AccountSettingsCommand)` in its `[Subcommand]` list, before `AccountLogoutCommand`. Every command works signed out and offline: none of them injects an API client, an authentication service or `ICommandContext`. A `UserSettingsException` ends a command with its message and `ExitCode` (2 for a damaged file, 1 for a file that cannot be read or written).

#### 5.1 `sform account settings`

`Commands/Account/Settings/AccountSettingsCommand.cs`, `[Command(SETTINGS, Description = "Show and change your local preferences of sform.")]`, with an extended help text that lists the keys and examples, and subcommands 5.2-5.4.

- Without options it prints a header, the file path, and a table with `Key`, `Value` and `Source` for every catalog entry. Every value, source and no-op decision comes from the effective sections of `store.Read()`, so a value of a legacy file counts only while its section's layer is `Legacy`. The values are those the commands use (`UserSettingValue.Resolve`):
  - the editor through the same `EditorResolution.Resolve` that `EditJsonAsync` uses (7): `$VISUAL` / `$EDITOR` as the source of both editor keys when no editor is configured, shown as `editor.type = custom`, `editor.command = <value>`;
  - the project and view as `CommandContext` picks them: `STORYTELLER_PROJECT` / `STORYTELLER_VIEW` as their source when set; with `STORYTELLER_PROJECT` the view is `default (STORYTELLER_PROJECT is set)`; a machine sign-in shows `(of the machine sign-in)` with the source `machine sign-in (<credential variables>)`, read from `MachineSignInSettings` (no sign-in, no API);
  - otherwise `settings`, `legacy editor.config.json` / `legacy access.default.json`, or `default` with the default display (`(ask on first use)`, `(not set)`, `house.main`, `default`);
  - a stored `editor.command` that another editor type ignores shows `settings (used by a custom editor only)`.
- `--path` prints only the file path (for scripts: `code "$(sform account settings --path)"`), also when the file is damaged.
- `--json` prints the values the commands use as a JSON object (`{"editor.type":"custom",…}`, `null` when there is none), like `story history --json`.
- A damaged file: its message and `ERROR_WRONG_INPUT`.

#### 5.2 `sform account settings editor [type] [-c|--command <cmd>]`

`AccountSettingsEditorCommand`, `[Command(EDITOR, Description = "Choose the editor that sform opens documents in; without a type it asks.")]`.

- Without a type and without `--command` it runs the existing picker (`IEditorService.PromptEditorPreference`) and saves the choice. With redirected input it does not prompt: it prints the usage and returns `ERROR_WRONG_INPUT`. A custom choice without a command saves nothing (`WARNING_ABORTED`).
- With `vscode`, `neovim` or `vim` it saves the type and removes `customCommand`.
- With `custom`, `--command` is required (else `ERROR_WRONG_INPUT`); both are saved.
- `--command` without a type implies `custom`; `--command` with another type is refused.
- An unknown type lists the allowed ones and returns `ERROR_WRONG_INPUT`.
- It prints the saved choice, and a warning when the executable of the choice is not found (`IEditorService.IsEditorAvailable`, 7); the value is saved anyway.
- The same value as the settings file already has: `WARNING_NO_WORK_NEEDED`, nothing written. A value that only the legacy file has is written, which migrates it.

#### 5.3 `sform account settings set <key> <value>`

`AccountSettingsSetCommand`, `[Command(SET, Description = "Set a preference: editor.type, editor.command, access.project or access.view.")]`. An unknown or missing key lists the known keys; a missing value, or one outside `AllowedValues` (or not in `ValueFormat`), lists the allowed values or the format; both return `ERROR_WRONG_INPUT`. Setting `editor.type` to anything but `custom` keeps `editor.command` in the file (it is ignored then), so switching back to `custom` brings it back. Prints `<key> = <value>`, with a hint when the result does nothing yet (`editor.command` for another type, `editor.type custom` without a command). The same value as the settings file already has gives `WARNING_NO_WORK_NEEDED`.

#### 5.4 `sform account settings unset <key>`

`AccountSettingsUnsetCommand`, `[Command(UNSET, Description = "Remove a preference, so sform uses its default again.")]`. An unknown key is refused as in `set`. Removes the properties of the key (migrating the legacy sections first, so an `unset` also hides a legacy value), also a value written by hand that `sform` cannot use; `WARNING_NO_WORK_NEEDED` when the effective section has none of them. `unset editor.type` makes the next edit use `$VISUAL`/`$EDITOR` or ask again.

### 6. Default project writers

- `AccountSetCommand`, `AccountRegisterCommand` and `AccessPointCreateCommand` save through `IUserSettingsStore.Update("access", …)` instead of `CreateAccessDefaultConfigFile`; they inject the store instead of `IFileSystem`. Their output does not change.
- `AccountSetCommand` otherwise keeps its behaviour (reads the account, offers the projects, `-p`/`-v`), and refuses a `-p` without both parts before any call, instead of throwing in the split.
- A store failure is reported with its message and the exception's exit code in `account set`; in `register` and `access-point create`, whose main work already succeeded, it is a warning (`The default project could not be saved: …`) and the exit code stays `SUCCESS`.
- `sform account settings` lists the default project and view too; `AccountCommand`'s hint to `sform account set` stays. The machine branch of `CommandContext` does not change.

### 7. Editor resolution and EditorService

- `Configuration/EnvironmentEditor`, captured once at startup like `GitHubActionsSettings`: `EnvironmentEditor.FromEnvironment(Func<string, string?>)` takes the first non-blank of `VISUAL` and `EDITOR`, with `Variable` and `Command`; registered as a singleton from `System.Environment.GetEnvironmentVariable`. The value is used as a custom command, as the issue asks (split as below, no shell expansion).
- `Configuration/EditorResolution.Resolve(EditorOptions configured, EnvironmentEditor environment)` returns the editor of an edit and its origin (`ResolvedEditor`, `EditorOrigin`): the configured one when `IsConfigured`, else the environment's as `EditorOptions { EditorType = Custom, CustomCommand = <value> }`, else none (ask). Both `EditorService` and `sform account settings` call it.
- `EditorService(IFileSystem fileSystem, IUserSettingsStore settings, EnvironmentEditor environment)`; `IEditorService` gains `PromptEditorPreference(console)` (the picker without saving) and `IsEditorAvailable(EditorOptions)`.
- `EditJsonAsync`:
  - with the environment's editor: uses it for this run without saving and prints a lowlight line `Using $EDITOR (code --wait); change it with sform account settings editor.`;
  - with none: the existing "No editor is configured yet" prompt (`SetupEditorPreference`); a custom choice without a command aborts the edit (`WARNING_ABORTED`);
  - an editor that cannot be started (`Win32Exception`) is reported as `The editor could not be started (<executable>): <reason>`. One from the environment then falls back to the picker, because the variables come before asking; a configured one ends with `ERROR_CRASH` and a hint to `sform account settings editor`, instead of the unhandled-exception output.
- `SetupEditorPreference` = `PromptEditorPreference` + save through `IUserSettingsStore.Update("editor", …)` (type and command in one write) + `Saved in <path>; change it with sform account settings editor.`; a store failure prints `Your choice could not be saved: <message>` and the edit goes on.
- Finding editors: `internal static FindExecutable(IFileSystem, executable, PATH, PATHEXT, isWindows)` looks a name up in the folders of the PATH (not the current folder), on Windows with each PATHEXT extension when it has none (`code` → `code.cmd`), and takes a path as it is. It replaces the `where`/`which` processes of the picker's detection and backs `IsEditorAvailable`, so detection and launch agree.
- Launch of a custom command: on Windows its executable is resolved with `FindExecutable` first, so `code --wait` starts `code.cmd`; `CreateNoWindow` is set only for the `cmd.exe` that starts VS Code, so a terminal editor of a custom command uses the console of `sform`.
- `ParseCustomCommand` (now `internal static`, on `SplitCustomCommand`) accepts a quoted executable: `"C:\Program Files\Notepad++\notepad++.exe" -multiInst -nosession` → executable `C:\Program Files\Notepad++\notepad++.exe`, arguments `-multiInst -nosession "<file>"` (single quotes too). An unquoted command keeps today's behaviour.
- The prompt `Enter the command to launch your editor (the config file path will be appended as argument)` gains `, e.g. code --wait`, because a GUI editor that does not wait makes `sform` read the unchanged file.
- Test seams: `OpenFileInEditorAsync` becomes `virtual` and the PATH lookup goes through `protected virtual IsOnPath`, so the tests start no process.

### 8. Registration

`Startup.ConfigureServices`: `IUserSettingsStore` → `UserSettingsStore` and `EnvironmentEditor` as singletons next to `IEditorService` (line 60). `ConfigureApplication` as in 3. The McMaster conventions pick up the new commands through the `[Subcommand]` attributes; nothing else to register.

### 9. Tests (`src/Platform/Cli/test`)

- `Configuration/UserSettingsStoreTests` (`MockFileSystem`, fixed paths as in `InstallationIdStoreTests`):
  - a missing or blank file loads as empty; a damaged one (bad JSON, an array, a section that is no object, trailing text) throws with the path; a `null` section is no section;
  - `Update` creates the folder, keeps unrelated properties and sections, replaces a property whatever its case, removes an emptied section (or keeps `{}` while an undeletable legacy file exists), leaves no temporary or lock file, keeps date-like text;
  - migration copies and normalises both legacy sections (number enum, `IsConfigured`, null command, PascalCase names; null access names), moves both on the first `Update` of either, deletes the legacy files, survives an undeletable one, skips a damaged one, and never overwrites a section the user file already has;
  - `Read` gives each section's layer; read and write failures become `UserSettingsException` with the right exit code.
- `Configuration/UserSettingsConfigurationTests`: the provider binds `EditorOptions` and `AccessDefaultOptions` from the user file, from the legacy files, the user section over the legacy one, an empty section hiding the legacy one, over `app.config.json`; only the known sections; an invalid `editorType` is ignored; a damaged user file warns and gives no settings; a store that cannot be created warns and stops nothing.
- `Configuration/EditorResolutionTests`: `$VISUAL` before `$EDITOR`, blank values ignored, configured wins, `Custom` without a command falls back.
- `Commands/AccountSettingsCommandsTests`: the list with each source (settings, legacy, shadowed legacy, `$VISUAL`/`$EDITOR`, `STORYTELLER_PROJECT`/`STORYTELLER_VIEW`, machine sign-in, an ignored command, a project without an organization); `--path` (also with a damaged file); `--json`; a damaged file; `editor` with each type, `custom` without `--command`, `--command` alone, `--command` with another type, an unknown type, redirected input without an argument, the picker, an unchanged value, a legacy value, a missing executable, a damaged file; `set`/`unset` with an unknown key, an invalid value, success, hints and no-op exit codes; `access.project` without both parts, and `unset access.project` keeping the view; the help text names every catalog key.
- `Commands/AccountDefaultProjectWritersTests`: `account set -p`, `account register` and `access-point create` save the default project through the store and move the legacy files; `account set -p` without both parts is refused before any call; a store failure gives its exit code in `set` and a warning with `SUCCESS` in the other two.
- `Services/EditorServiceTests` (a test subclass that starts no process): the environment editor used and not saved, a configured editor over the environment, the picker saving through the store and offering the installed editors, the prompt's example, a custom choice without a command, a save failure not stopping the edit, a configured editor that cannot start, an environment editor that cannot start falling back to the picker, `ParseCustomCommand` with and without quotes, `FindExecutable` with PATHEXT, with an extension, with a path and on Unix.

### 10. Out of scope

- An `$SFORM_EDITOR` variable or a per-project settings file: no request for them yet.
- Moving `auth.discovery.json`, a cache that may be lost.
- Finding legacy files in other version folders of the tool (see 3), and running `$VISUAL` / `$EDITOR` through a shell (`sh -c`) as git does.

### 11. Documentation

- `docs/42for.net/cli/sform.md`: a new **Settings** section after **Telemetry**: where the file lives (the same folder as `telemetry.json`), the table of keys, the commands with examples, the sources of the list, the `$VISUAL`/`$EDITOR` fallback, that an old `editor.config.json` or `access.default.json` next to the running `sform` is still read and moved on the first change (and that `dotnet tool update` from an older version already removed them), and that comments in the file are not kept. The **Sign in** section mentions `sform account set` and that the default project is kept in the same file.
- `docs/Platform/Storyteller/authentication.md`: line 408 names the default project in `sform.settings.json` instead of `access.default.json`; line 263 says that `auth.discovery.json` is kept next to the `sform` assembly. The comments of `CommandContext`, `CommandContextTests` and `AuthenticationDiscoveryCache` follow.
- A review in `docs/Platform/Cli/specs/reviews/` after the implementation.
