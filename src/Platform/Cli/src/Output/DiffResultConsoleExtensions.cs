using System;
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
