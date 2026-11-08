using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Initializes a new instance of <see cref="EditorService"/>.
    /// </summary>
    /// <param name="fileSystem">The file system of the temporary files.</param>
    /// <param name="settings">The settings store that keeps the chosen editor.</param>
    public EditorService(IFileSystem fileSystem, IUserSettingsStore settings)
    {
        _fileSystem = fileSystem;
        _settings = settings;
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

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = isTerminalEditor,
            CreateNoWindow = !isTerminalEditor,
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
        // 1. Ensure editor is configured
        if (!options.IsConfigured)
        {
            console.WriteImportant("No editor is configured yet. Let's set one up.");
            options = SetupEditorPreference(console);

            if (!options.IsConfigured)
            {
                console.WriteImportant("No editor command was entered, edit aborted.");
                return new JsonEditResult(null, ExitCodes.WARNING_ABORTED);
            }
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
                var exitCode = await OpenFileInEditorAsync(tempFilePath, options);

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
    /// Detects which supported editors are available on the current system by checking for their executables.
    /// </summary>
    /// <returns>A set containing one or more <see cref="EditorType"/> values for editors found on the system (possible values: <c>VsCode</c>, <c>Neovim</c>, <c>Vim</c>).</returns>
    private static HashSet<EditorType> DetectAvailableEditors()
    {
        var available = new HashSet<EditorType>();
        var detectCommand = OperatingSystem.IsWindows() ? "where" : "which";

        if (IsCommandAvailable(detectCommand, "code"))
        {
            available.Add(EditorType.VsCode);
        }

        if (IsCommandAvailable(detectCommand, "nvim"))
        {
            available.Add(EditorType.Neovim);
        }

        if (IsCommandAvailable(detectCommand, "vim"))
        {
            available.Add(EditorType.Vim);
        }

        return available;
    }

    /// <summary>
    /// Checks whether a given command is available on the system by running the specified discovery executable with the target command as an argument.
    /// </summary>
    /// <param name="detectCommand">The discovery executable to run (for example, "where" on Windows or "which" on Unix).</param>
    /// <param name="targetCommand">The command name to look up (for example, "code", "nvim", or "vim").</param>
    /// <returns>`true` if the discovery process exits with code 0 within the timeout period, `false` otherwise.</returns>
    private static bool IsCommandAvailable(string detectCommand, string targetCommand)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = detectCommand,
                Arguments = targetCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit();
                }
                catch
                {
                    // Ignore exceptions from Kill
                }

                return false;
            }

            try
            {
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parse a user-provided custom command into the executable name and its arguments, always appending the provided file path as a quoted argument.
    /// </summary>
    /// <param name="customCommand">The custom command string entered by the user; may include the executable and optional base arguments.</param>
    /// <param name="filePath">The file path to append to the command's arguments; it will be quoted.</param>
    /// <returns>
    /// A tuple where `command` is the executable (the first token of <paramref name="customCommand"/>)
    /// and `arguments` are the remaining base arguments followed by the quoted <paramref name="filePath"/>.
    /// If <paramref name="customCommand"/> contains no space, `arguments` will contain only the quoted file path.
    /// </returns>
    private static (string command, string arguments) ParseCustomCommand(string customCommand, string filePath)
    {
        var trimmed = customCommand.Trim();
        var spaceIndex = trimmed.IndexOf(' ');

        if (spaceIndex < 0)
        {
            return (trimmed, $"\"{filePath}\"");
        }

        var command = trimmed[..spaceIndex];
        var baseArgs = trimmed[(spaceIndex + 1)..].Trim();
        return (command, $"{baseArgs} \"{filePath}\"");
    }
}
