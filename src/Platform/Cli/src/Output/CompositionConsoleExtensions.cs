using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Output;

public static class CompositionConsoleExtensions
{
    /// <summary>
    /// The columns of the step table. The key in 'Applied at' takes the width that is left and wraps in it, so the short
    /// columns keep their text on one line in an 80-column terminal.
    /// </summary>
    public static IReadOnlyList<IHeaderColumn> StepColumns { get; } =
    [
        new HeaderColumn("Step"),
        new HeaderColumn("Applied at", GridLength.Star(1)),
        new HeaderColumn("Source"),
        new HeaderColumn("Version"),
        new HeaderColumn("Presence"),
        new HeaderColumn("Values"),
    ];

    /// <summary>
    /// Writes the steps of a composition as a table, in the order of application, then the effective document with the
    /// step that set each line (the blame listing).
    /// </summary>
    public static void WriteComposition(this IExtendedConsole @this, ConfigurationComposition composition)
    {
        @this.WriteHeader($"Composition of the configuration for '{composition.AnnotationKey}'");
        @this.WriteTable(GetStepRows(composition), StepColumns);

        @this.WriteLine();
        @this.WriteHeader("Effective configuration, by step");

        foreach (var (text, _, step) in CompositionLines.Of(ToJObject(composition.Content), composition.Origins ?? []))
        {
            // no margin and a wrap by character: Alba drops characters where it breaks a line inside a margin
            @this.WriteExactDocument(new Document($"{step,3} │ ".ThemedLowlight(@this.Theme), text)
            {
                Color = @this.Theme.ForegroundColor,
                TextWrap = TextWrap.CharWrap,
            });
            @this.WriteLine();
        }
    }

    /// <summary>
    /// One row per step: the order, the key of the node that applies it indented by its depth, the source, its version
    /// and presence, and the number of values of the effective document that the step set.
    /// </summary>
    public static IReadOnlyList<string[]> GetStepRows(ConfigurationComposition composition)
    {
        var nodes = new Dictionary<int, ConfigurationCompositionNode>();
        var sources = new Dictionary<string, ConfigurationCompositionSource>(StringComparer.Ordinal);

        foreach (var node in composition.Nodes ?? [])
        {
            nodes.TryAdd(node.Index, node);
        }

        foreach (var source in composition.Sources ?? [])
        {
            sources.TryAdd(source.Id, source);
        }

        var values = (composition.Origins ?? [])
            .GroupBy(origin => origin.Step)
            .ToDictionary(group => group.Key, group => group.Count());

        return (composition.Steps ?? [])
            .OrderBy(step => step.Order)
            .Select(step =>
            {
                var node = nodes.GetValueOrDefault(step.Node);
                var source = sources.GetValueOrDefault(step.SourceId);

                return new[]
                {
                    step.Order.ToString(CultureInfo.InvariantCulture),
                    new string(' ', (node?.Depth ?? 0) * 2) + (node?.AnnotationKey ?? string.Empty),
                    FormatSource(source, step.SourceId),
                    FormatVersion(source),
                    FormatPresence(source),
                    values.GetValueOrDefault(step.Order).ToString(CultureInfo.InvariantCulture),
                };
            })
            .ToList();
    }

    // "template Execution" or "configuration"; the id when the source is not listed.
    private static string FormatSource(ConfigurationCompositionSource? source, string sourceId)
    {
        return source?.Kind switch
        {
            ConfigurationCompositionSourceKind.Template => $"template {source.AnnotationType}",
            ConfigurationCompositionSourceKind.Configuration => "configuration",
            _ => sourceId,
        };
    }

    // "v3", or "—" for a missing configuration.
    private static string FormatVersion(ConfigurationCompositionSource? source)
    {
        return source is null || source.Presence == ConfigurationCompositionSourcePresence.Missing
            ? "—"
            : $"v{source.Version.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatPresence(ConfigurationCompositionSource? source)
    {
        return source?.Presence.ToString().ToLowerInvariant() ?? string.Empty;
    }

    // The SDK hands the document over as an object: a JObject at runtime.
    private static JObject ToJObject(object? content)
    {
        return content switch
        {
            null => new JObject(),
            JObject jObject => jObject,
            _ => JObject.FromObject(content),
        };
    }
}
