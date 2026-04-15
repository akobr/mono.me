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

    public EditorService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

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

    public async Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options)
    {
        var (command, arguments) = options.EditorType switch
        {
            Configuration.EditorType.VsCode => ("code", $"--wait \"{filePath}\""),
            Configuration.EditorType.Neovim => ("nvim", $"\"{filePath}\""),
            Configuration.EditorType.Vim => ("vim", $"\"{filePath}\""),
            Configuration.EditorType.Custom => ParseCustomCommand(options.CustomCommand ?? string.Empty, filePath),
            _ => throw new InvalidOperationException("Editor type is not configured."),
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = true,
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            return 1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

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

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

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

    private void SaveEditorOptions(EditorOptions options)
    {
        var config = new { editor = options };
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented });
        var appDirectory = _fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
        var fullPath = _fileSystem.Path.Combine(appDirectory, Constants.EDITOR_CONFIG_JSON);
        using var fileWriter = _fileSystem.File.CreateText(fullPath);
        var jWriter = new JsonTextWriter(fileWriter);
        serializer.Serialize(jWriter, config);
    }
}
