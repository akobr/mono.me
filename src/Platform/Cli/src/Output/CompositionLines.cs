using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Output;

// The lines of the indented JSON of an effective document, each with the pointer of its value and the step that set it.
// The text is written here, two spaces per level as Newtonsoft indents it, so a line and its value always match.
public static class CompositionLines
{
    /// <summary>
    /// One entry per line: a line that opens or closes a container, or holds a scalar, takes the step of that value,
    /// looked up by its RFC 6901 pointer; the root braces have none.
    /// </summary>
    public static IReadOnlyList<(string Text, string Pointer, int? Step)> Of(JObject content, IEnumerable<ConfigurationCompositionOrigin> origins)
    {
        var steps = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var origin in origins)
        {
            steps.TryAdd(origin.Path, origin.Step);
        }

        var lines = new List<(string Text, string Pointer, int? Step)>();
        AddValue(lines, steps, content, string.Empty, string.Empty, 0, string.Empty);
        return lines;
    }

    // The prefix is the quoted name and colon of a property, empty for an array item and the root; the comma follows
    // every value but the last of its container.
    private static void AddValue(
        List<(string Text, string Pointer, int? Step)> lines,
        IReadOnlyDictionary<string, int> steps,
        JToken value,
        string prefix,
        string pointer,
        int depth,
        string comma)
    {
        var indent = new string(' ', depth * 2);
        int? step = pointer.Length > 0 && steps.TryGetValue(pointer, out var origin) ? origin : null;

        switch (value)
        {
            case JObject @object when @object.HasValues:
                var properties = @object.Properties().ToList();
                lines.Add(($"{indent}{prefix}{{", pointer, step));

                for (var index = 0; index < properties.Count; index++)
                {
                    var property = properties[index];
                    AddValue(
                        lines,
                        steps,
                        property.Value,
                        $"{JsonConvert.ToString(property.Name)}: ",
                        $"{pointer}/{EscapePointerToken(property.Name)}",
                        depth + 1,
                        index < properties.Count - 1 ? "," : string.Empty);
                }

                lines.Add(($"{indent}}}{comma}", pointer, step));
                break;

            case JArray array when array.HasValues:
                lines.Add(($"{indent}{prefix}[", pointer, step));

                for (var index = 0; index < array.Count; index++)
                {
                    AddValue(
                        lines,
                        steps,
                        array[index],
                        string.Empty,
                        $"{pointer}/{index}",
                        depth + 1,
                        index < array.Count - 1 ? "," : string.Empty);
                }

                lines.Add(($"{indent}]{comma}", pointer, step));
                break;

            case JObject:
                lines.Add(($"{indent}{prefix}{{}}{comma}", pointer, step));
                break;

            case JArray:
                lines.Add(($"{indent}{prefix}[]{comma}", pointer, step));
                break;

            default:
                lines.Add(($"{indent}{prefix}{value.ToString(Formatting.None)}{comma}", pointer, step));
                break;
        }
    }

    // "~" as "~0" and "/" as "~1", in this order.
    private static string EscapePointerToken(string name)
    {
        return name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }
}
