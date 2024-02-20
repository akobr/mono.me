using System;
using System.IO.Abstractions;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller;
using Newtonsoft.Json;
using Sharprompt;

namespace _42.Platform.Cli.Output;

public static class ExtendedConsoleExtensions
{
    public static void WriteJson(this IExtendedConsole @this, object data)
    {
        using var jWriter = new JsonTextWriter(@this.Console.Out);
        jWriter.CloseOutput = false;
        var serializer = JsonSerializer.CreateDefault();
        serializer.Serialize(jWriter, data);
        Console.WriteLine();
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
