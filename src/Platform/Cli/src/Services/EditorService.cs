using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Reflection;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using Newtonsoft.Json;
using Sharprompt;

namespace _42.Platform.Cli.Services;

public class EditorService : IEditorService
{
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of <see cref="EditorService"/> and stores the provided file system for use in file and path operations.
    /// </summary>
    public EditorService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Interactively prompts the user to choose an editor for editing JSON configuration and saves the selected preference.
    /// </summary>
    /// <param name="console">Console used to display the selection prompt and to read input for a custom command.</param>
    /// <returns>The constructed <see cref="EditorOptions"/> reflecting the chosen editor; if a custom command was chosen, <see cref="EditorOptions.CustomCommand"/> contains the entered command.</returns>
    public EditorOptions SetupEditorPreference(IExtendedConsole console)
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
                    Message = "Enter the command to launch your editor (the config file path will be appended as argument)",
                }),
            },
        };

        SaveEditorOptions(options);
        return options;
    }

    /// <summary>
    /// Opens the specified file in the editor described by <paramref name="options"/> and returns the editor process's exit code.
    /// </summary>
    /// <param name="filePath">The path of the file to open in the editor.</param>
    /// <param name="options">Editor selection and configuration to use when launching the editor.</param>
    /// <returns>`1` if the editor process could not be started; otherwise the editor process exit code.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="Configuration.EditorType"/> in <paramref name="options"/> is not configured.</exception>
    public async Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options)
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

    /// <summary>
    /// Persists the provided editor options to the application's editor configuration JSON file.
    /// </summary>
    /// <param name="options">The editor configuration to save; it will be stored under the top-level property "editor".</param>
    /// <remarks>The file is written as indented JSON to the path [assembly directory or current directory]/Constants.EDITOR_CONFIG_JSON.</remarks>
    private void SaveEditorOptions(EditorOptions options)
    {
        var config = new { editor = options };
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented });
        var appDirectory = _fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
        var fullPath = _fileSystem.Path.Combine(appDirectory, Constants.EDITOR_CONFIG_JSON);
        using var fileWriter = _fileSystem.File.CreateText(fullPath);
        using var jWriter = new JsonTextWriter(fileWriter);
        serializer.Serialize(jWriter, config);
    }
}