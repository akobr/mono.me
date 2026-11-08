using System;
using System.Globalization;
using System.Text;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using Spectre.Console;

namespace _42.Platform.Cli.Output;

public static class DiffResultConsoleExtensions
{
    /// <summary>
    /// Writes a diff returned by the API as colorized hunks with line numbers and highlighted word changes.
    /// </summary>
    public static void WriteDiffResult(this IExtendedConsole @this, DiffResult diff)
    {
        if (diff.Hunks == null || diff.Hunks.Count == 0)
        {
            @this.WriteLine("No changes detected.");
            return;
        }

        // Stats header
        @this.WriteLine();
        AnsiConsole.MarkupLine($"[green]+{diff.Stats.Additions}[/] [red]-{diff.Stats.Deletions}[/] [dim]~{diff.Stats.Unchanged}[/]");
        @this.WriteLine();

        foreach (var hunk in diff.Hunks)
        {
            AnsiConsole.MarkupLine($"[cyan]@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@[/]");

            var maxLineNum = Math.Max(
                hunk.OldStart + hunk.OldCount,
                hunk.NewStart + hunk.NewCount);
            var numWidth = maxLineNum.ToString().Length;

            foreach (var line in hunk.Lines)
            {
                var oldNum = line.OldLineNumber?.ToString().PadLeft(numWidth) ?? new string(' ', numWidth);
                var newNum = line.NewLineNumber?.ToString().PadLeft(numWidth) ?? new string(' ', numWidth);

                AnsiConsole.Markup($"[grey]{oldNum} {newNum}[/] [dim]│[/] ");

                switch (line.Type)
                {
                    case DiffLineType.Addition:
                        RenderLineWithSegments(line, "+", "green");
                        break;
                    case DiffLineType.Deletion:
                        RenderLineWithSegments(line, "-", "red");
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[dim]  {Markup.Escape(line.Content)}[/]");
                        break;
                }
            }

            @this.WriteLine();
        }
    }

    /// <summary>
    /// Writes a diff returned by the API as the plain unified text of the API's <c>?format=unified</c>: a hunk header
    /// <c>@@ -a,b +c,d @@</c>, then each line with its prefix '+', '-' or ' '. The text goes to the standard output as it
    /// is, in UTF-8 with LF line ends, to be piped; "No changes detected." goes to the standard error.
    /// </summary>
    public static void WriteUnifiedDiff(this IExtendedConsole @this, DiffResult diff)
    {
        var text = ToUnifiedDiff(diff);

        if (text.Length == 0)
        {
            @this.WriteErrorLine("No changes detected.");
            return;
        }

        using var encoding = @this.UseUtf8Output();
        @this.WriteRaw(text);
    }

    // As DiffFormatter.ToUnifiedDiff of the API (Abstractions.Annotations), with LF line ends.
    internal static string ToUnifiedDiff(DiffResult diff)
    {
        var text = new StringBuilder();

        foreach (var hunk in diff.Hunks ?? [])
        {
            text.Append(CultureInfo.InvariantCulture, $"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@").Append('\n');

            foreach (var line in hunk.Lines ?? [])
            {
                var prefix = line.Type switch
                {
                    DiffLineType.Addition => '+',
                    DiffLineType.Deletion => '-',
                    _ => ' ',
                };

                text.Append(prefix).Append(line.Content).Append('\n');
            }
        }

        return text.ToString();
    }

    private static void RenderLineWithSegments(DiffLine line, string prefix, string baseColor)
    {
        if (line.Segments == null || line.Segments.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{baseColor}]{Markup.Escape($"{prefix} {line.Content}")}[/]");
            return;
        }

        AnsiConsole.Markup($"[{baseColor}]{Markup.Escape($"{prefix} ")}[/]");

        foreach (var segment in line.Segments)
        {
            if (segment.IsChange)
            {
                AnsiConsole.Markup($"[bold underline {baseColor}]{Markup.Escape(segment.Text)}[/]");
            }
            else
            {
                AnsiConsole.Markup($"[{baseColor}]{Markup.Escape(segment.Text)}[/]");
            }
        }

        AnsiConsole.WriteLine();
    }
}
