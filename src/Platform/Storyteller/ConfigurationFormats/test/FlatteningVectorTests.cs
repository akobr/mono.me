using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// conformance/flattening/flat.json, shared with the providers of other languages; FormatVectorTests runs the other
// targets.
public class FlatteningVectorTests
{
    private static IReadOnlyDictionary<string, JObject> Vectors => ConformanceFiles.Load(ConformanceFiles.Flat);

    public static TheoryData<string> Names()
    {
        return ConformanceFiles.Names(ConformanceFiles.Flat);
    }

    // A target that this runner does not implement fails here rather than being skipped.
    [Fact]
    public void EveryFlatteningFile_HasATargetOfThisRunner()
    {
        var files = ConformanceFiles.OfKind("flattening");

        foreach (var file in files)
        {
            var target = ConformanceFiles.Read(file).Value<string>("target")!;

            ConformanceFiles.Targets.ShouldContainKey(target, $"{file} has the target {target}, which this runner does not implement.");
            ConformanceFiles.Targets[target].ShouldBe(file);
        }

        files.ShouldBe(ConformanceFiles.Targets.Values, ignoreOrder: true);
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

        // In document order, which the body keeps and the vectors pin for every runtime.
        body.Properties().ShouldAllBe(property => property.Value.Type == JTokenType.String);
        body.Properties()
            .Select(property => (property.Name, property.Value.Value<string>()!))
            .ShouldBe(vector["keys"].ShouldBeOfType<JObject>().Properties().Select(property => (property.Name, property.Value.Value<string>()!)));
    }

    private static Dictionary<string, string> ExpectedKeys(JObject vector)
    {
        return vector["keys"].ShouldBeOfType<JObject>().Properties()
            .ToDictionary(property => property.Name, property => property.Value.Value<string>()!, StringComparer.Ordinal);
    }
}
