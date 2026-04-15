using System;
using System.Collections.Generic;
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

    public static void WriteDiff(this IExtendedConsole @this, string originalJson, string editedJson)
    {
        var originalLines = originalJson.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var editedLines = editedJson.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var maxLines = Math.Max(originalLines.Length, editedLines.Length);
        var numWidth = maxLines.ToString().Length;
        var hasDifferences = false;

        Console.WriteLine();

        // Simple line-by-line comparison with longest common subsequence
        var lcs = ComputeLcs(originalLines, editedLines);
        var oi = 0;
        var ei = 0;
        var li = 0;

        while (oi < originalLines.Length || ei < editedLines.Length)
        {
            if (li < lcs.Count
                && oi < originalLines.Length
                && ei < editedLines.Length
                && originalLines[oi] == lcs[li]
                && editedLines[ei] == lcs[li])
            {
                // Context line (unchanged)
                AnsiConsole.Markup($"[grey]{(ei + 1).ToString().PadLeft(numWidth)}[/] [dim]│[/] ");
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(originalLines[oi])}[/]");
                oi++;
                ei++;
                li++;
            }
            else if (oi < originalLines.Length && (li >= lcs.Count || originalLines[oi] != lcs[li]))
            {
                // Removed line
                hasDifferences = true;
                AnsiConsole.Markup($"[red]{(oi + 1).ToString().PadLeft(numWidth)}[/] [red]│[/] ");
                AnsiConsole.MarkupLine($"[red]- {Markup.Escape(originalLines[oi])}[/]");
                oi++;
            }
            else if (ei < editedLines.Length && (li >= lcs.Count || editedLines[ei] != lcs[li]))
            {
                // Added line
                hasDifferences = true;
                AnsiConsole.Markup($"[green]{(ei + 1).ToString().PadLeft(numWidth)}[/] [green]│[/] ");
                AnsiConsole.MarkupLine($"[green]+ {Markup.Escape(editedLines[ei])}[/]");
                ei++;
            }
        }

        if (!hasDifferences)
        {
            AnsiConsole.MarkupLine("[dim]No differences found.[/]");
        }

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

    private static List<string> ComputeLcs(string[] a, string[] b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        var result = new List<string>();
        var x = m;
        var y = n;

        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                result.Add(a[x - 1]);
                x--;
                y--;
            }
            else if (dp[x - 1, y] > dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        result.Reverse();
        return result;
    }
}
