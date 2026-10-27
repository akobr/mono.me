using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// Vectors/flattening.json, shared with the providers of other languages.
public class FlatteningVectorTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, JObject>> Vectors = new(LoadVectors);

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();

        foreach (var name in Vectors.Value.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    [Fact]
    public void TheFile_HasVectorsOfBothKinds()
    {
        Vectors.Value.Values.ShouldContain(vector => vector["keys"] != null);
        Vectors.Value.Values.ShouldContain(vector => vector["error"] != null);
        Vectors.Value.Values.ShouldAllBe(vector => (vector["keys"] == null) != (vector["error"] == null));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Vector(string name)
    {
        var vector = Vectors.Value[name];
        var document = vector["document"];

        if (vector["error"] is JObject error)
        {
            var exception = Should.Throw<InvalidDocumentException>(() => DocumentFlattener.Flatten(document));

            exception.Rule.ShouldBe(error.Value<string>("rule"));
            exception.Path.ShouldBe(error.Value<string>("path"));
            exception.Message.ShouldContain(exception.Path);
            return;
        }

        var expected = vector["keys"].ShouldBeOfType<JObject>().Properties()
            .ToDictionary(property => property.Name, property => property.Value.Value<string>()!, StringComparer.Ordinal);

        var keys = DocumentFlattener.Flatten(document);

        // The exact text of every key: the flattener's dictionary itself compares case-insensitively.
        keys.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal).ShouldBe(expected, ignoreOrder: true);
    }

    // Read as the SDK reads a body: date-like strings stay strings, numbers are doubles.
    private static Dictionary<string, JObject> LoadVectors()
    {
        using var stream = typeof(FlatteningVectorTests).Assembly.GetManifestResourceStream("Vectors.flattening.json")
            ?? throw new InvalidOperationException("The flattening vectors are not embedded.");
        using var reader = new JsonTextReader(new StreamReader(stream))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };

        return JObject.Load(reader)["vectors"]!
            .Cast<JObject>()
            .ToDictionary(vector => vector.Value<string>("name")!, StringComparer.Ordinal);
    }
}
