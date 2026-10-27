using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// Vectors/flattening.json, shared with the providers of other languages.
public class FlatteningVectorTests
{
    private static IReadOnlyDictionary<string, JObject> Vectors => VectorFiles.Load(VectorFiles.Flattening);

    public static TheoryData<string> Names()
    {
        return VectorFiles.Names(VectorFiles.Flattening);
    }

    [Fact]
    public void TheFile_HasVectorsOfBothKinds()
    {
        Vectors.Values.ShouldContain(vector => vector["keys"] != null);
        Vectors.Values.ShouldContain(vector => vector["error"] != null);
        Vectors.Values.ShouldAllBe(vector => (vector["keys"] == null) != (vector["error"] == null));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Vector(string name)
    {
        var vector = Vectors[name];
        var document = vector["document"];

        if (vector["error"] is JObject error)
        {
            var exception = Should.Throw<InvalidDocumentException>(() => DocumentFlattener.Flatten(document));

            exception.Rule.ShouldBe(error.Value<string>("rule"));
            exception.Path.ShouldBe(error.Value<string>("path"));
            exception.Message.ShouldContain(exception.Path);
            return;
        }

        var keys = DocumentFlattener.Flatten(document);

        // The exact text of every key: the flattener's dictionary itself compares case-insensitively.
        keys.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal).ShouldBe(ExpectedKeys(vector), ignoreOrder: true);
    }

    // The flat format passes the vectors with its output: the body parsed back as JSON.
    [Theory]
    [MemberData(nameof(Names))]
    public void Vector_AsAFlatBody(string name)
    {
        var vector = Vectors[name];
        var document = vector["document"];

        if (vector["error"] is JObject error)
        {
            var exception = Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.Format(document, ConfigurationFormat.Flat));

            exception.Rule.ShouldBe(error.Value<string>("rule"));
            exception.Path.ShouldBe(error.Value<string>("path"));
            return;
        }

        var body = SdkJson.Parse(ConfigurationFormatter.Format(document, ConfigurationFormat.Flat).Text).ShouldBeOfType<JObject>();

        body.Properties().ShouldAllBe(property => property.Value.Type == JTokenType.String);
        body.Properties()
            .ToDictionary(property => property.Name, property => property.Value.Value<string>()!, StringComparer.Ordinal)
            .ShouldBe(ExpectedKeys(vector), ignoreOrder: true);
    }

    private static Dictionary<string, string> ExpectedKeys(JObject vector)
    {
        return vector["keys"].ShouldBeOfType<JObject>().Properties()
            .ToDictionary(property => property.Name, property => property.Value.Value<string>()!, StringComparer.Ordinal);
    }
}
