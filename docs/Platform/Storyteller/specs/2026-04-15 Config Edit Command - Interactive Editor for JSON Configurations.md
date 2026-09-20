# Config Edit Command
## Problem
The CLI currently supports `config get` (download + display), `config set` (upload from file/inline properties), and `config delete`. There is no interactive editing workflow — users must manually export, edit externally, and re-import. We need an `edit` subcommand that opens the configuration in the user's preferred editor and handles the full round-trip.
## Current State
* `ConfigGetCommand` is a subcommand of `StorytellerListCommand`, with `ConfigSetCommand` and `ConfigDeleteCommand` as its own subcommands.
* The API client (`IConfigurationApiClient`) provides `GetConfigurationAsync`, `SetConfigurationAsync`, and `GetConfigurationVersionDiffAsync`.
* The `Configuration` model has `Content` (object), `Version`, `AnnotationKey`, `Author`, `Hash`, `Labels`, `Values`.
* User preferences are stored as JSON files next to the assembly (same pattern as `AccessDefaultOptions` / `access.default.json`). Options are loaded via `IOptions<T>` bound from `IConfiguration`.
* Prompting (select, confirm, input) is available via `IPrompter` on `IExtendedConsole` (uses Sharprompt).
* `IFileSystem` abstraction is used throughout for testability.
* No `Process.Start` usage exists in the CLI yet.
## Proposed Changes
### 1. Editor Preferences Model & Persistence
Create `src/Platform/Cli/src/Configuration/EditorOptions.cs`:
* `EditorType` enum: `VsCode`, `Neovim`, `Vim`, `Custom`
* `EditorOptions` class with `EditorType` and `CustomCommand` (string) properties
* Persisted in `editor.config.json` next to the assembly (same pattern as `access.default.json`)
* Add `EDITOR` constant to `ConfigurationSections`
* Add `editor.config.json` constant to `Constants.cs`
* Register with `services.Configure<EditorOptions>` in `Startup.cs` and load the JSON in `ConfigureApplication`
### 2. Editor Detection & Setup Service
Create `src/Platform/Cli/src/Services/EditorService.cs` (+ interface `IEditorService`):
* `DetectAvailableEditors()` — checks if `code` (VS Code) and `nvim`/`vim` are on PATH using `Process.Start` with `where` (Windows) / `which` (Unix)
* `SetupEditorPreference(IExtendedConsole console)` — interactive prompt flow:
    1. Auto-detect available editors
    2. Build selection list: "Visual Studio Code" (if found), "Neovim" (if found), "Vim" (if found), "Custom command"
    3. If "Custom", prompt for the command string
    4. Save choice to `editor.config.json`
* `OpenFileInEditorAsync(string filePath, EditorOptions options)` — launches the editor process and waits for it to exit. For VS Code uses `code --wait <file>`, for neovim/vim runs directly, for custom uses the configured command with the file path appended as argument
* Register as singleton in `Startup.cs`
### 3. Config Edit Command
Create `src/Platform/Cli/src/Commands/Configuration/ConfigEditCommand.cs`:
* Registered as a new subcommand on `ConfigGetCommand` alongside `ConfigSetCommand` and `ConfigDeleteCommand`
* Add `EDIT` constant to `CommandNames`
* Command: `[Command(CommandNames.EDIT, Description = "Edit a configuration in your preferred editor.")]`
* Extends `BaseContextCommand`
* Dependencies: `IConfigurationApiClient`, `IFileSystem`, `IEditorService`, `IOptions<EditorOptions>`
* Argument: `AnnotationKey` (same as other config commands)
* Execution flow:
    1. Check if editor is configured; if not, run `EditorService.SetupEditorPreference()` (first-run experience)
    2. Try to fetch existing configuration via `GetConfigurationAsync`. If 404, start with empty `{}` JSON
    3. Serialize the `Content` to a pretty-printed JSON temp file (use `IFileSystem`, put in system temp dir with a meaningful name like `config-{annotationKey}.json`)
    4. Save original JSON string for later diff comparison
    5. Open the editor via `EditorService.OpenFileInEditorAsync()` and wait for it to close
    6. Read the edited file content
    7. Validate it is valid JSON (try `JObject.Parse`); if invalid, show error with details and ask user whether to re-open the editor or abort
    8. If content is unchanged from original, report "No changes detected" and exit
    9. Compute and display a line-by-line diff between original and edited JSON (colored: green for additions, red for removals — using Spectre.Console markup)
    10. Prompt user to confirm upload
    11. On confirm, call `SetConfigurationAsync` with the parsed JObject
    12. Clean up temp file
### 4. Diff Display Utility
Add a static method in `ExtendedConsoleExtensions` (or a new small helper in `Output/`):
* `WriteDiff(this IExtendedConsole, string originalJson, string editedJson)` — performs line-by-line comparison of the two pretty-printed JSON strings and renders colored output (red for removed lines, green for added lines, grey for context)
