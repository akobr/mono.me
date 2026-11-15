using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The files the project settings tests read, embedded by the test project: the project-settings kind of the conformance
// suite (src/Platform/Storyteller/conformance/project-settings.json) and the settings of the exhibit data set.
internal static class ProjectSettingsFiles
{
    public const string Conformance = "Conformance/project-settings.json";
    public const string Exhibit = "DataSets/exhibit/project-settings.json";

    public static string ReadText(string name)
    {
        using var stream = typeof(ProjectSettingsFiles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The file {name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static JsonObject Read(string name)
    {
        return JsonNode.Parse(ReadText(name))!.AsObject();
    }

    public static IReadOnlyDictionary<string, JsonObject> Vectors()
    {
        return Read(Conformance)["vectors"]!.AsArray()
            .Select(vector => vector!.AsObject())
            .ToDictionary(vector => vector["name"]!.GetValue<string>(), StringComparer.Ordinal);
    }
}
