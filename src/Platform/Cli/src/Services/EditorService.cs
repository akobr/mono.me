using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharprompt;

namespace _42.Platform.Cli.Services;

public class EditorService : IEditorService
{
    // As deep as the configurations client reads (ApiSdk.partial.cs): the API stores up to 128 levels.
    private const int MaxDocumentDepth = 256;

    private readonly IFileSystem _fileSystem;
    private readonly IUserSettingsStore _settings;
    private readonly EnvironmentEditor _environment;

    /// <summary>
    /// Initializes a new instance of <see cref="EditorService"/>.
    /// </summary>
    /// <param name="fileSystem">The file system of the temporary files.</param>
    /// <param name="settings">The settings store that keeps the chosen editor.</param>
    /// <param name="environment">The editor of $VISUAL or $EDITOR, used when none is configured.</param>
    public EditorService(IFileSystem fileSystem, IUserSettingsStore settings, EnvironmentEditor environment)
    {
        _fileSystem = fileSystem;
        _settings = settings;
        _environment = environment;
    }

    /// <summary>
    /// Interactively prompts the user to choose an editor and saves the choice in the settings file. A choice that cannot be
    /// saved is reported and still returned, so the edit goes on with it.
    /// </summary>
    /// <param name="console">Console used to display the selection prompt and to read input for a custom command.</param>
    /// <returns>The chosen editor; not configured when a custom command was chosen without entering one.</returns>
    public EditorOptions SetupEditorPreference(IExtendedConsole console)
    {
        var options = PromptEditorPreference(console);

        if (!options.IsConfigured)
        {
            return options;
        }

        try
        {
            _settings.Update(ConfigurationSections.EDITOR, UserSettingsSchema.EditorChanges(options));
            console.WriteLine($"Saved in {_settings.FilePath}; change it with sform account settings editor.".ThemedLowlight(console.Theme));
        }
        catch (UserSettingsException exception)
        {
            console.WriteImportant($"Your choice could not be saved: {exception.Message}");
        }

        return options;
    }

    /// <summary>
    /// Interactively prompts the user to choose an editor, without saving the choice.
    /// </summary>
    /// <param name="console">Console used to display the selection prompt and to read input for a custom command.</param>
    /// <returns>The constructed <see cref="EditorOptions"/> reflecting the chosen editor; if a custom command was chosen, <see cref="EditorOptions.CustomCommand"/> contains the entered command.</returns>
    public EditorOptions PromptEditorPreference(IExtendedConsole console)
    {
        console.EnsureInteractive(
            "the editor to use",
            "Choose one with sform account settings editor in a terminal, set it with sform account settings set editor.type, or set $VISUAL or $EDITOR.");

        var availableEditors = DetectAvailableEditors();
        var selectionItems = new List<string>();

        if (availableEditors.Contains(EditorType.VsCode))
        {
            selectionItems.Add("Visual Studio Code");
        }

        if (availableEditors.Contains(EditorType.Neovim))
        {
            selectionItems.Add("Neovim");
        }

        if (availableEditors.Contains(EditorType.Vim))
        {
            selectionItems.Add("Vim");
        }

        selectionItems.Add("Custom command");

        var selected = console.Select(new SelectOptions<string>
        {
            Message = "Select your preferred editor for JSON configuration editing",
            Items = selectionItems,
        });

        var options = selected switch
        {
            "Visual Studio Code" => new EditorOptions { EditorType = Configuration.EditorType.VsCode },
            "Neovim" => new EditorOptions { EditorType = Configuration.EditorType.Neovim },
            "Vim" => new EditorOptions { EditorType = Configuration.EditorType.Vim },
            _ => new EditorOptions
            {
                EditorType = Configuration.EditorType.Custom,
                CustomCommand = console.Input<string>(new InputOptions<string>
                {
                    Message = "Enter the command to launch your editor (the config file path will be appended as argument), e.g. code --wait",
                })?.Trim(),
            },
        };

        return options;
    }

    /// <summary>
    /// Whether the executable of an editor is found: on the PATH, or as the file a custom command names by its path.
    /// </summary>
    /// <param name="options">The editor to look for.</param>
    /// <returns><c>true</c> when the executable is found.</returns>
    public bool IsEditorAvailable(EditorOptions options)
    {
        var executable = DescribeExecutable(options);

        return !string.IsNullOrWhiteSpace(executable) && IsOnPath(executable);
    }

    /// <summary>
    /// Opens the specified file in the editor described by <paramref name="options"/> and returns the editor process's exit code.
    /// </summary>
    /// <param name="filePath">The path of the file to open in the editor.</param>
    /// <param name="options">Editor selection and configuration to use when launching the editor.</param>
    /// <returns>`1` if the editor process could not be started; otherwise the editor process exit code.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="Configuration.EditorType"/> in <paramref name="options"/> is not configured.</exception>
    public virtual async Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options)
    {
        var (command, arguments) = options.EditorType switch
        {
            Configuration.EditorType.VsCode => OperatingSystem.IsWindows()
                ? ("cmd.exe", $"/c code --wait \"{filePath}\"")
                : ("code", $"--wait \"{filePath}\""),
            Configuration.EditorType.Neovim => ("nvim", $"\"{filePath}\""),
            Configuration.EditorType.Vim => ("vim", $"\"{filePath}\""),
            Configuration.EditorType.Custom => ParseCustomCommand(options.CustomCommand ?? string.Empty, filePath),
            _ => throw new InvalidOperationException("Editor type is not configured."),
        };

        var isTerminalEditor = options.EditorType == Configuration.EditorType.Neovim ||
                               options.EditorType == Configuration.EditorType.Vim;

        // A command of quotes alone (""): reported like an editor that is not installed, not as a crash of Process.Start.
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new Win32Exception(2, "The command line names no executable.");
        }

        // Windows starts only an .exe by its bare name: code of a custom command or of $EDITOR is code.cmd on the PATH.
        if (options.EditorType == Configuration.EditorType.Custom && OperatingSystem.IsWindows())
        {
            command = FindExecutable(command) ?? command;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = isTerminalEditor,

            // Only the cmd.exe that starts VS Code is hidden: a custom editor may be a terminal editor (vim, nano), which
            // needs the console of sform; a GUI editor opens no console anyway.
            CreateNoWindow = options.EditorType == Configuration.EditorType.VsCode,
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            return 1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>
    /// Lets the user edit a JSON document in the preferred editor, validates it, shows the changes and asks for a confirmation.
    /// </summary>
    /// <param name="confirm">Whether to ask before accepting the edited document. The default is <c>true</c>.</param>
    /// <param name="check">
    /// Checks the parsed document: its problem re-opens the editor, and its document replaces the parsed one for the
    /// comparison, the diff, the question and the result.
    /// </param>
    /// <returns>
    /// The edited document, or null with an exit code:
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when no changes were made;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user aborted the operation;
    /// - <c>ExitCodes.ERROR_CRASH</c> when the editor exited with a non-zero code.
    /// </returns>
    public async Task<JsonEditResult> EditJsonAsync(IExtendedConsole console, EditorOptions options, JObject original, string fileNamePrefix, bool isNew, bool confirm = true, Func<JObject, JsonEditCheck>? check = null)
    {
        // 1. The configured editor, else the one of $VISUAL or $EDITOR for this run, else ask and save the choice
        var resolved = EditorResolution.Resolve(options, _environment);

        switch (resolved.Origin)
        {
            case EditorOrigin.Configured:
                break;
            case EditorOrigin.Environment:
                options = resolved.Options!;
                console.WriteLine($"Using ${resolved.Variable} ({options.CustomCommand}); change it with sform account settings editor.".ThemedLowlight(console.Theme));
                break;
            default:
                console.WriteImportant("No editor is configured yet. Let's set one up.");
                options = SetupEditorPreference(console);

                if (!options.IsConfigured)
                {
                    console.WriteImportant("No editor command was entered, edit aborted.");
                    return new JsonEditResult(null, ExitCodes.WARNING_ABORTED);
                }

                break;
        }

        // 2. Write to a temp file
        var originalJson = original.ToString(Formatting.Indented);
        var tempFilePath = CreateTempFilePath(_fileSystem, fileNamePrefix);

        await _fileSystem.File.WriteAllTextAsync(tempFilePath, originalJson, Encoding.UTF8);

        try
        {
            // 3. Open editor loop (allows re-editing on invalid JSON, or on a document the check rejects)
            JObject edited;

            while (true)
            {
                int exitCode;

                try
                {
                    exitCode = await OpenFileInEditorAsync(tempFilePath, options);
                }
                catch (Win32Exception exception)
                {
                    // An editor that is not installed, or a command line that names no executable.
                    console.WriteImportant($"The editor could not be started ({DescribeExecutable(options)}): {exception.Message}");

                    // $VISUAL and $EDITOR come before asking, so asking stays the way out of a variable that does not work.
                    if (resolved.Origin == EditorOrigin.Environment)
                    {
                        resolved = resolved with { Origin = EditorOrigin.None };
                        options = SetupEditorPreference(console);

                        if (options.IsConfigured)
                        {
                            continue;
                        }

                        console.WriteImportant("No editor command was entered, edit aborted.");
                        return new JsonEditResult(null, ExitCodes.WARNING_ABORTED);
                    }

                    console.WriteLine("Choose another one with sform account settings editor.".ThemedLowlight(console.Theme));
                    return new JsonEditResult(null, ExitCodes.ERROR_CRASH);
                }

                if (exitCode != 0)
                {
                    console.WriteImportant($"Editor exited with code {exitCode}.");
                    return new JsonEditResult(null, ExitCodes.ERROR_CRASH);
                }

                var editedJson = _fileSystem.File.ReadAllText(tempFilePath, Encoding.UTF8);
                var (document, problem) = ReadDocument(editedJson, check);

                if (document is not null)
                {
                    edited = document;
                    break;
                }

                console.WriteImportant(problem!);

                // Without a terminal there is nobody to fix it in the editor again.
                if (!console.IsInteractive())
                {
                    console.WriteLine("Nothing was saved.");
                    return new JsonEditResult(null, ExitCodes.ERROR_WRONG_INPUT);
                }

                var shouldRetry = console.Confirm(new ConfirmOptions
                {
                    Message = "Would you like to re-open the editor to fix the issue",
                    DefaultValue = true,
                });

                if (!shouldRetry)
                {
                    console.WriteLine("Edit aborted.");
                    return new JsonEditResult(null, ExitCodes.WARNING_ABORTED);
                }
            }

            // 4. Compare the documents (not the formatting), as the check gave it
            if (JToken.DeepEquals(original, edited))
            {
                console.WriteLine("No changes detected.");
                return new JsonEditResult(null, ExitCodes.WARNING_NO_WORK_NEEDED);
            }

            // 5. Show diff
            ShowChanges(console, isNew, originalJson, edited.ToString(Formatting.Indented));

            // 6. Confirm upload
            if (confirm)
            {
                console.EnsureInteractive("the confirmation of the changes", "Pass -y|--yes to save them without asking.");

                var shouldSave = console.Confirm(new ConfirmOptions
                {
                    Message = "Do you want to save these changes",
                    DefaultValue = true,
                });

                if (!shouldSave)
                {
                    console.WriteLine("Edit aborted.");
                    return new JsonEditResult(null, ExitCodes.WARNING_ABORTED);
                }
            }

            return new JsonEditResult(edited, ExitCodes.SUCCESS);
        }
        finally
        {
            // 7. Clean up temp file
            try
            {
                if (_fileSystem.File.Exists(tempFilePath))
                {
                    _fileSystem.File.Delete(tempFilePath);
                }
            }
            catch (IOException ex)
            {
                console.WriteImportant($"Could not delete temp file '{tempFilePath}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                console.WriteImportant($"Could not delete temp file '{tempFilePath}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A new path in the temporary folder for a document, named after <paramref name="fileNamePrefix"/>. Only letters,
    /// digits, '.', '-' and '_' are kept: an annotation name may hold '"' or '%', which would break the quoted path on the
    /// editor's command line (cmd.exe expands %…% inside quotes).
    /// </summary>
    internal static string CreateTempFilePath(IFileSystem fileSystem, string fileNamePrefix)
    {
        var safePrefix = string.Concat(fileNamePrefix.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            $"{safePrefix}-{fileSystem.Path.GetRandomFileName()[..8]}.json");
    }

    /// <summary>
    /// Parses an edited document as <see cref="JObject.Parse(string)"/> does (comments allowed, nothing after the object),
    /// but as the SDK reads documents: date-like strings stay strings (<c>DateParseHandling.None</c>), which parsed would
    /// never equal the original strings and would be written back in the offset of this machine, and up to the depth of
    /// the configurations client. A property twice in one object is an error, not its last value.
    /// </summary>
    /// <exception cref="JsonReaderException">The text is not one JSON object.</exception>
    internal static JObject ParseDocument(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = MaxDocumentDepth,
        };

        var document = JObject.Load(reader, new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        });

        while (reader.Read())
        {
            // only comments may follow: the reader throws on anything else after the object
        }

        // What Newtonsoft reads beyond JSON, and would write back changed or not at all.
        var notJson = document
            .DescendantsAndSelf()
            .FirstOrDefault(token => token.Type is JTokenType.Undefined or JTokenType.Constructor
                                     || (token.Type == JTokenType.Float && token.Value<double>() is var number && !double.IsFinite(number)));

        if (notJson is not null)
        {
            throw new JsonReaderException($"The value at '{notJson.Path}' is not JSON (undefined, a constructor, NaN or Infinity).");
        }

        return document;
    }

    /// <summary>
    /// Shows the changes the user is asked to save: the original and the edited document, as the check gave it.
    /// </summary>
    protected virtual void ShowChanges(IExtendedConsole console, bool isNew, string originalJson, string editedJson)
    {
        console.WriteHeader(isNew ? "New document" : "Changes");
        console.WriteDiff(originalJson, editedJson);
    }

    // The document when it is valid JSON and the check accepts it, as the check gave it; otherwise the problem to show.
    private static (JObject? Document, string? Problem) ReadDocument(string json, Func<JObject, JsonEditCheck>? check)
    {
        JObject document;

        try
        {
            document = ParseDocument(json);
        }
        catch (JsonReaderException exception)
        {
            return (null, $"Invalid JSON: {exception.Message}");
        }

        if (check is null)
        {
            return (document, null);
        }

        var result = check(document);
        return (result.Document, result.Document is null ? result.Problem ?? "The document is not valid." : null);
    }

    /// <summary>
    /// Parse a user-provided custom command into the executable name and its arguments, always appending the provided file path as a quoted argument.
    /// </summary>
    /// <param name="customCommand">The custom command string entered by the user; may include the executable and optional base arguments.</param>
    /// <param name="filePath">The file path to append to the command's arguments; it will be quoted.</param>
    /// <returns>
    /// A tuple where `command` is the executable (the first token of <paramref name="customCommand"/>, or the quoted
    /// path it starts with) and `arguments` are the remaining base arguments followed by the quoted <paramref name="filePath"/>.
    /// </returns>
    internal static (string Command, string Arguments) ParseCustomCommand(string customCommand, string filePath)
    {
        var (executable, baseArguments) = SplitCustomCommand(customCommand);
        return baseArguments.Length == 0
            ? (executable, $"\"{filePath}\"")
            : (executable, $"{baseArguments} \"{filePath}\"");
    }

    /// <summary>
    /// Splits a custom command into its executable and its arguments (see <see cref="CustomEditorCommand.Split"/>).
    /// </summary>
    /// <param name="customCommand">The custom command.</param>
    /// <returns>The executable without quotes and the trimmed arguments, empty when there are none.</returns>
    internal static (string Executable, string Arguments) SplitCustomCommand(string customCommand)
    {
        return CustomEditorCommand.Split(customCommand);
    }

    /// <summary>
    /// Finds the file that a command starts, as the shell does: a path as it is, else the first folder of the PATH that has
    /// it, on Windows with each extension of PATHEXT when it has none (code is code.cmd), in lower case as the files usually
    /// are named. The current folder is not searched.
    /// </summary>
    /// <param name="fileSystem">The file system to look in.</param>
    /// <param name="executable">The executable of the command, a name or a path.</param>
    /// <param name="searchPath">The PATH variable.</param>
    /// <param name="pathExtensions">The PATHEXT variable; Windows only.</param>
    /// <param name="isWindows">Whether the folders and extensions are those of Windows.</param>
    /// <returns>The path of the file, or null when there is none.</returns>
    internal static string? FindExecutable(IFileSystem fileSystem, string executable, string? searchPath, string? pathExtensions, bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        IReadOnlyList<string> extensions = isWindows && !fileSystem.Path.HasExtension(executable)
            ? (string.IsNullOrWhiteSpace(pathExtensions) ? ".COM;.EXE;.BAT;.CMD" : pathExtensions)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => extension.ToLowerInvariant())
                .ToList()
            : [string.Empty];
        var hasFolder = executable.IndexOfAny(['/', '\\']) >= 0;
        IEnumerable<string> folders = hasFolder
            ? [string.Empty]
            : (searchPath ?? string.Empty).Split(isWindows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var folder in folders)
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = hasFolder ? executable + extension : fileSystem.Path.Combine(folder.Trim('"'), executable + extension);

                    if (fileSystem.File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // A folder of the PATH that is no valid path.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a command is found on the PATH, as the shell finds it.
    /// </summary>
    /// <param name="command">The command name to look up (for example, "code", "nvim", or "vim").</param>
    /// <returns><c>true</c> when the command is found.</returns>
    protected virtual bool IsOnPath(string command)
    {
        return FindExecutable(command) is not null;
    }

    /// <summary>
    /// Detects which supported editors are available on the current system by checking for their executables.
    /// </summary>
    /// <returns>A set containing one or more <see cref="EditorType"/> values for editors found on the system (possible values: <c>VsCode</c>, <c>Neovim</c>, <c>Vim</c>).</returns>
    private HashSet<EditorType> DetectAvailableEditors()
    {
        var available = new HashSet<EditorType>();

        if (IsOnPath("code"))
        {
            available.Add(EditorType.VsCode);
        }

        if (IsOnPath("nvim"))
        {
            available.Add(EditorType.Neovim);
        }

        if (IsOnPath("vim"))
        {
            available.Add(EditorType.Vim);
        }

        return available;
    }

    // The file that a command name starts, from the PATH of sform.
    private string? FindExecutable(string executable)
    {
        return FindExecutable(
            _fileSystem,
            executable,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"),
            OperatingSystem.IsWindows());
    }

    // The executable that an editor starts, as the messages name it.
    private static string DescribeExecutable(EditorOptions options)
    {
        return options.EditorType switch
        {
            Configuration.EditorType.VsCode => "code",
            Configuration.EditorType.Neovim => "nvim",
            Configuration.EditorType.Vim => "vim",
            Configuration.EditorType.Custom => SplitCustomCommand(options.CustomCommand ?? string.Empty).Executable,
            _ => string.Empty,
        };
    }
}
