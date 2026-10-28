using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// The conformance suite, src/Platform/Storyteller/conformance, shared with the implementations of other languages and
// embedded as Conformance/<path>. suite.json lists the files with their kind; a flattening file names its target; a
// vector file is { description, vectors: [{ name, document, ... }] }.
internal static class ConformanceFiles
{
    public const string Flat = "flattening/flat.json";
    public const string Properties = "flattening/properties.json";
    public const string Env = "flattening/env.json";
    public const string Formats = "formats.json";

    private const string Manifest = "suite.json";
    private const string Prefix = "Conformance/";

    private static readonly ConcurrentDictionary<string, JObject> Files = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, JObject>> Vectors = new(StringComparer.Ordinal);

    // The file of every target this runner implements; a manifest with another target fails FlatteningVectorTests.
    public static IReadOnlyDictionary<string, string> Targets { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["flat"] = Flat,
        ["properties"] = Properties,
        ["env"] = Env,
    };

    // The files of a kind, as suite.json lists them.
    public static IReadOnlyList<string> OfKind(string kind)
    {
        return Read(Manifest)["files"]!
            .Where(file => file.Value<string>("kind") == kind)
            .Select(file => file.Value<string>("path")!)
            .ToList();
    }

    // A whole file.
    public static JObject Read(string path)
    {
        return Files.GetOrAdd(path, ReadResource);
    }

    // The vectors of a file by name.
    public static IReadOnlyDictionary<string, JObject> Load(string path)
    {
        return Vectors.GetOrAdd(path, file => Read(file)["vectors"]!
            .Cast<JObject>()
            .ToDictionary(vector => vector.Value<string>("name")!, StringComparer.Ordinal));
    }

    public static TheoryData<string> Names(string path)
    {
        var names = new TheoryData<string>();

        foreach (var name in Load(path).Keys)
        {
            names.Add(name);
        }

        return names;
    }

    // Read as the SDK reads a body. The logical names hold the separator of the OS that built the tests.
    private static JObject ReadResource(string path)
    {
        var assembly = typeof(ConformanceFiles).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(name => name.Replace('\\', '/') == Prefix + path)
            ?? throw new InvalidOperationException($"The conformance file {path} is not embedded.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        return (JObject)SdkJson.Read(new StreamReader(stream));
    }
}
