using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// The files of Configuration/test/Vectors, embedded as Vectors.<file> and shared with the implementations of other
// languages: { description, vectors: [{ name, document, ... }] }.
internal static class VectorFiles
{
    public const string Flattening = "flattening.json";
    public const string Properties = "flattening.properties.json";
    public const string Env = "flattening.env.json";
    public const string Formats = "formats.json";

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, JObject>> Files = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, JObject> Load(string file)
    {
        return Files.GetOrAdd(file, Read);
    }

    public static TheoryData<string> Names(string file)
    {
        var names = new TheoryData<string>();

        foreach (var name in Load(file).Keys)
        {
            names.Add(name);
        }

        return names;
    }

    // Read as the SDK reads a body.
    private static IReadOnlyDictionary<string, JObject> Read(string file)
    {
        using var stream = typeof(VectorFiles).Assembly.GetManifestResourceStream($"Vectors.{file}")
            ?? throw new InvalidOperationException($"The vectors {file} are not embedded.");

        return SdkJson.Read(new StreamReader(stream))["vectors"]!
            .Cast<JObject>()
            .ToDictionary(vector => vector.Value<string>("name")!, StringComparer.Ordinal);
    }
}
