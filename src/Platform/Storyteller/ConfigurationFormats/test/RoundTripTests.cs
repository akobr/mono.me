using System.Globalization;
using _42.Platform.Storyteller.ConfigurationFormats.UnitTests.Readers;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// Every format read back by a reader of its own: YamlDotNet for yaml, the java.util.Properties rules for properties,
// the dotenv dialect for env and a JSON reader for flat. Each gives the keys and values of DocumentFlattener.
public class RoundTripTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, JToken>> Documents = new(CollectDocuments);

    public static TheoryData<string> DocumentNames()
    {
        var names = new TheoryData<string>();

        foreach (var name in Documents.Value.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    // Flattening the parsed YAML gives exactly the flat keys; strings are quoted, numbers and booleans plain.
    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void TheYaml_ParsesBackToTheFlatKeys(string name)
    {
        var document = Documents.Value[name];
        var leaves = DocumentFlattener.Read(ConfigurationFormatter.Canonicalize(document));

        var parsed = YamlReader.Flatten(ConfigurationFormatter.Format(document, ConfigurationFormat.Yaml).Text);

        parsed.Keys.ShouldBe(leaves.Select(Key), ignoreOrder: true);

        foreach (var leaf in leaves)
        {
            parsed[Key(leaf)].ShouldBe((leaf.Value, leaf.Kind != DocumentValueKind.String), leaf.Path);
        }
    }

    [Fact]
    public void EveryCharacter_OfAValue_SurvivesEveryFormat()
    {
        var value = EveryCharacter();
        var content = new JObject { ["value"] = value };

        SdkJson.Parse(Format(content, ConfigurationFormat.Flat)).Value<string>("value").ShouldBe(value);
        PropertiesReader.Read(Format(content, ConfigurationFormat.Properties)).ShouldBe([("value", value)]);
        EnvReader.Read(Format(content, ConfigurationFormat.Env)).ShouldBe([("VALUE", value)]);
        YamlReader.Flatten(Format(content, ConfigurationFormat.Yaml)).ShouldBe(
            new Dictionary<string, (string, bool)> { ["value"] = (value, false) });
    }

    [Fact]
    public void EveryCharacter_OfAName_SurvivesFlatPropertiesAndYaml()
    {
        // Without ':', which no format allows, and without the brackets that properties refuses.
        var name = EveryCharacter().Replace(":", string.Empty, StringComparison.Ordinal);
        var content = new JObject { [name] = "x" };
        var bracketless = name.Replace("[", string.Empty, StringComparison.Ordinal).Replace("]", string.Empty, StringComparison.Ordinal);

        SdkJson.Parse(Format(content, ConfigurationFormat.Flat)).Value<string>(name).ShouldBe("x");
        PropertiesReader.Read(Format(new JObject { [bracketless] = "x" }, ConfigurationFormat.Properties))
            .ShouldBe([($"[{bracketless}]", "x")]);
        YamlReader.Flatten(Format(content, ConfigurationFormat.Yaml)).ShouldBe(
            new Dictionary<string, (string, bool)> { [name] = ("x", false) });
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public void AYamlKey_LongerThan1024Characters_IsExplicit(int length, bool isExplicit)
    {
        var plain = new string('k', length);
        var quoted = new string(' ', length - 2);
        var content = new JObject { [plain] = 1, ["list"] = new JArray(new JObject { [quoted] = new JObject { ["a"] = "x" } }) };

        var yaml = Format(content, ConfigurationFormat.Yaml);

        yaml.StartsWith("? ", StringComparison.Ordinal).ShouldBe(isExplicit);
        yaml.Contains($"- ? \"{quoted}\"\n", StringComparison.Ordinal).ShouldBe(isExplicit);
        YamlReader.Flatten(yaml).ShouldBe(
            new Dictionary<string, (string, bool)> { [plain] = ("1", true), [$"list:0:{quoted}:a"] = ("x", false) });
    }

    private static string Key(DocumentLeaf leaf)
    {
        return string.Join(':', leaf.Segments.Select(segment => segment.Name ?? segment.Index.ToString(CultureInfo.InvariantCulture)));
    }

    private static string Format(JToken content, ConfigurationFormat format)
    {
        return ConfigurationFormatter.Format(content, format).Text;
    }

    // U+0000 to U+024F, the separators, the byte order mark, the non-characters, U+FFFD and a surrogate pair.
    private static string EveryCharacter()
    {
        var characters = Enumerable.Range(0, 0x250).Select(code => (char)code).ToList();
        characters.AddRange(['\u2028', '\u2029', '\uFEFF', '\uFFFD', '\uFFFE', '\uFFFF', '\uD83D', '\uDE00']);
        return new string([.. characters]);
    }

    // The valid documents of the vectors, and a few more.
    private static Dictionary<string, JToken> CollectDocuments()
    {
        var documents = new Dictionary<string, JToken>(StringComparer.Ordinal);

        foreach (var file in new[] { VectorFiles.Flattening, VectorFiles.Properties, VectorFiles.Env, VectorFiles.Formats })
        {
            foreach (var (name, vector) in VectorFiles.Load(file))
            {
                if (vector["document"] is JObject document && IsValidForYaml(document))
                {
                    documents[$"{file}: {name}"] = document;
                }
            }
        }

        documents["reserved and quoted keys"] = SdkJson.Parse(
            """
            {
              "y": 1, "Yes": 2, "NO": 3, "on": 4, "OFF": 5, "true": 6, "null": 7, "~": 8, "-": 9, "? x": 10,
              "#c": 11, "&d": 12, "*e": 13, "!f": 14, "|g": 15, ">h": 16, "%i": 17, "@j": 18, "`k": 19,
              "'l'": 20, "\"m\"": 21, "n o": 22, " p": 23, "q ": 24
            }
            """);
        documents["numbers"] = SdkJson.Parse("""{ "a": 1E+20, "b": 1E-05, "c": -0.0, "d": 123456789012345678901234567890, "e": 0.1 }""");
        documents["deep gaps"] = SdkJson.Parse("""{ "a": [ [ null, [ null, { "b": [ null, null, 1 ] } ] ], null, { "c": [ {}, [], 2 ] } ] }""");

        return documents;
    }

    private static bool IsValidForYaml(JObject document)
    {
        try
        {
            DocumentFlattener.Read(document);
            return true;
        }
        catch (InvalidDocumentException)
        {
            return false;
        }
    }
}
