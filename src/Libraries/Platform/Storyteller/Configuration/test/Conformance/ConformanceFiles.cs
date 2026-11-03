using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Conformance;

// The conformance suite, src/Platform/Storyteller/conformance, shared with the providers and SDKs of other languages
// and embedded as Conformance/<path>: suite.json, the schemas, and the vector files suite.json lists with their kind.
internal static class ConformanceFiles
{
    public const string Manifest = "suite.json";
    public const string SchemaFolder = "schema/";

    private const string Prefix = "Conformance/";

    // The logical names hold the separator of the OS that built the tests.
    private static readonly Dictionary<string, string> Resources = typeof(ConformanceFiles).Assembly.GetManifestResourceNames()
        .Where(name => name.Replace('\\', '/').StartsWith(Prefix, StringComparison.Ordinal))
        .ToDictionary(name => name.Replace('\\', '/')[Prefix.Length..], StringComparer.Ordinal);

    // Every embedded path, schemas and manifest included.
    public static IReadOnlyCollection<string> Embedded => Resources.Keys;

    // The files of suite.json with their kind.
    public static IReadOnlyList<(string Path, string Kind)> Listed()
    {
        return Read(Manifest)["files"]!
            .Select(file => (file.Value<string>("path")!, file.Value<string>("kind")!))
            .ToList();
    }

    public static IReadOnlyList<string> OfKind(string kind)
    {
        return Listed().Where(file => file.Kind == kind).Select(file => file.Path).ToList();
    }

    public static string ReadText(string path)
    {
        var resource = Resources.GetValueOrDefault(path)
            ?? throw new InvalidOperationException($"The conformance file {path} is not embedded.");

        using var stream = typeof(ConformanceFiles).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Read as the SDK reads a body (date-like strings stay strings, numbers are doubles); a duplicate name, which
    // JSON.parse would silently resolve to the last value, fails.
    public static JObject Read(string path)
    {
        using var reader = new JsonTextReader(new StringReader(ReadText(path)))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };

        return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    }

    // The named items of a file: vectors, or the scenarios of a refresh file.
    public static IReadOnlyDictionary<string, JObject> Load(string path, string collection = "vectors")
    {
        return Read(path)[collection]!
            .Cast<JObject>()
            .ToDictionary(item => item.Value<string>("name")!, StringComparer.Ordinal);
    }

    public static TheoryData<string> Names(string path, string collection = "vectors")
    {
        var names = new TheoryData<string>();

        foreach (var name in Load(path, collection).Keys)
        {
            names.Add(name);
        }

        return names;
    }
}
