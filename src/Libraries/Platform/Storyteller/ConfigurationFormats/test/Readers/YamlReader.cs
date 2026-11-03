using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests.Readers;

// Parses YAML with YamlDotNet and flattens it as DocumentFlattener flattens JSON: mapping keys and sequence indexes
// joined with ':', a plain null makes no key. Every scalar comes with whether it was plain (a number or a boolean).
internal static class YamlReader
{
    public static IReadOnlyDictionary<string, (string Value, bool IsPlain)> Flatten(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        var keys = new Dictionary<string, (string Value, bool IsPlain)>(StringComparer.Ordinal);
        stream.Documents.Count.ShouldBe(1);
        Visit(stream.Documents[0].RootNode, null, keys);
        return keys;
    }

    private static void Visit(YamlNode node, string? key, Dictionary<string, (string Value, bool IsPlain)> keys)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (name, value) in mapping.Children)
                {
                    var text = name.ShouldBeOfType<YamlScalarNode>().Value!;
                    Visit(value, key is null ? text : $"{key}:{text}", keys);
                }

                break;

            case YamlSequenceNode sequence:
                for (var index = 0; index < sequence.Children.Count; index++)
                {
                    Visit(sequence.Children[index], $"{key}:{index}", keys);
                }

                break;

            case YamlScalarNode scalar when scalar.Style == ScalarStyle.Plain && scalar.Value == "null":
                break;

            case YamlScalarNode scalar:
                keys.Add(key!, (scalar.Value!, scalar.Style == ScalarStyle.Plain));
                break;
        }
    }
}
