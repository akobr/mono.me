using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller;
using Newtonsoft.Json;
using Sharprompt;
using Spectre.Console;
using Spectre.Console.Json;

namespace _42.Platform.Cli.Output;

public static class ExtendedConsoleExtensions
{
    public static void WriteJson(this IExtendedConsole @this, object data)
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        using var jWriter = new JsonTextWriter(sw) { Formatting = Formatting.Indented, CloseOutput = false };
        JsonSerializer.CreateDefault().Serialize(jWriter, data);

        var json = sb.ToString();

        // Render JsonText to a capture buffer so we can prepend line numbers.
        var captureWriter = new StringWriter();
        var captureConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(captureWriter),
            ColorSystem = ColorSystemSupport.TrueColor,
            Ansi = AnsiSupport.Yes,
        });
        captureConsole.Write(new JsonText(json));

        var lines = captureWriter.ToString()
            .TrimEnd('\r', '\n')
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var numWidth = lines.Length.ToString().Length;

        Console.WriteLine();
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            AnsiConsole.Markup($"[grey]{(index + 1).ToString().PadLeft(numWidth)}[/] [dim]│[/] ");
            Console.Write(line);
            Console.WriteLine();
        }
    }

    public static void WriteJsonToFile(this IExtendedConsole @this, object data, string filePath, IFileSystem fileSystem)
    {
        Console.WriteLine();

        if (fileSystem.Path.Exists(filePath))
        {
            if (fileSystem.Directory.Exists(filePath))
            {
                Console.WriteLine("Export is not possible, the path points to existing folder.");
                return;
            }

            if (fileSystem.File.Exists(filePath))
            {
                var shouldOverride = @this.Confirm(new ConfirmOptions
                {
                    Message = $"The file '{fileSystem.Path.GetFileName(filePath)}' already exists. Do you want to rewrite it",
                    DefaultValue = true,
                });

                if (!shouldOverride)
                {
                    return;
                }
            }
        }

        using var fileWriter = fileSystem.File.CreateText(filePath);
        using var jWriter = new JsonTextWriter(fileWriter);
        var serializer = JsonSerializer.CreateDefault();
        serializer.Serialize(jWriter, data);
        Console.WriteLine();
    }

    public static AnnotationKey ValidateAnnotationKey(this IExtendedConsole @this, string rawAnnotationKey)
    {
        if (string.IsNullOrWhiteSpace(rawAnnotationKey)
            || !AnnotationKey.TryParse(rawAnnotationKey, out var annotationKey))
        {
            @this.WriteImportant("Please specify a valid annotation key.");
            @this.WriteLine(
                "If you need assistance, please call ".ThemedLowlight(@this.Theme),
                "sform assist key",
                " command.".ThemedLowlight(@this.Theme));

            throw new WrongInputException();
        }

        return annotationKey;
    }
}
