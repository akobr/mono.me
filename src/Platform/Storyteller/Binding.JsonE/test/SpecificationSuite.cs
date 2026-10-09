using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

/// <summary>
/// Loads the JSON-e specification suite (json-e <c>specification.yml</c>) and json-everything's extra cases.
/// Each YAML document with a <c>title</c> is one case.
/// </summary>
internal static class SpecificationSuite
{
    public const string Now = "2017-01-19T16:27:20.974Z";

    private static readonly Lazy<IReadOnlyList<SpecificationCase>> LazyCases = new(Load);

    public static IReadOnlyList<SpecificationCase> Cases => LazyCases.Value;

    public static IEnumerable<object[]> CaseIndexes()
    {
        return Enumerable.Range(0, Cases.Count).Select(index => new object[] { index });
    }

    private static IReadOnlyList<SpecificationCase> Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Specification");
        return ReadFile(Path.Combine(directory, "specification.yml"), "spec")
            .Concat(ReadFile(Path.Combine(directory, "more-tests.yml"), "more"))
            .ToList();
    }

    private static IEnumerable<SpecificationCase> ReadFile(string path, string source)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(File.ReadAllText(path)))
        {
            stream.Load(reader);
        }

        var index = 0;
        foreach (var document in stream.Documents)
        {
            if (document.RootNode is not YamlMappingNode root || ToNode(root) is not JsonObject node)
            {
                continue;
            }

            if (node["title"]?.GetValue<string>() is not { } title)
            {
                continue;
            }

            index++;
            yield return new SpecificationCase(
                $"{source} #{index}: {title}",
                node["template"],
                node["context"] as JsonObject ?? new JsonObject(),
                node.ContainsKey("result") ? node["result"] : null,
                node.ContainsKey("result"),
                node["error"] is not null || node["panic"] is not null);
        }
    }

    private static JsonNode? ToNode(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
            {
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    obj[((YamlScalarNode)key).Value ?? string.Empty] = ToNode(value);
                }

                return obj;
            }

            case YamlSequenceNode sequence:
            {
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                {
                    array.Add(ToNode(item));
                }

                return array;
            }

            case YamlScalarNode scalar:
                return ToScalar(scalar);

            default:
                throw new InvalidOperationException($"Unsupported YAML node {node.NodeType}.");
        }
    }

    private static JsonNode? ToScalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? string.Empty;
        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
        {
            return JsonValue.Create(text);
        }

        // Plain scalars follow the YAML 1.2 core schema.
        switch (text)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            (char.IsDigit(text[0]) || text[0] is '-' or '+' or '.'))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(text);
    }
}
