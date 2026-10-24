using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

// Fixtures/composition-execution.json is the 'execution' literal of the admin UI test
// src/Platform/ui.admin/src/test/unit/configuration/configuration-composition.test.ts, ported to JSON: the server's
// composition of the execution of docs/Platform/Storyteller/inheritance.md (the currency example) with an execution
// template, where the subject is applied through the usage (step 2) and again through the context (step 4).
internal static class Compositions
{
    public const string ExecutionKey = "exe.northwind.invoicing.prod";
    public const string Subject = "configuration:sbt.northwind";
    public const string Usage = "configuration:usg.northwind.invoicing";
    public const string Context = "configuration:cnt.northwind.prod";
    public const string Responsibility = "configuration:rst.invoicing";
    public const string Template = "template:exe";
    public const string Own = "configuration:exe.northwind.invoicing.prod";

    private static readonly JsonSerializerSettings Settings = new() { DateParseHandling = DateParseHandling.None };

    public static string ExecutionJson()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "composition-execution.json"));
    }

    public static Sdk.ConfigurationComposition Execution()
    {
        return JsonConvert.DeserializeObject<Sdk.ConfigurationComposition>(ExecutionJson(), Settings)!;
    }

    public static JObject Content(this Sdk.ConfigurationComposition composition)
    {
        return (JObject)composition.Content;
    }

    // The stored document of a source, created (and the source made Present) when it is Missing.
    public static JObject SourceContent(this Sdk.ConfigurationComposition composition, string id)
    {
        var source = composition.Sources.Single(item => item.Id == id);

        if (source.Content is not JObject content)
        {
            content = new JObject();
            source.Content = content;
            source.Presence = Sdk.ConfigurationCompositionSourcePresence.Present;
            source.Version = 1;
        }

        return content;
    }

    public static void AddOrigin(this Sdk.ConfigurationComposition composition, string path, int step)
    {
        composition.Origins.Add(new Sdk.ConfigurationCompositionOrigin { Path = path, Step = step });
    }

    // The execution without its template: the view has none for exe.
    public static Sdk.ConfigurationComposition ExecutionWithoutTemplate()
    {
        var composition = Execution();
        composition.Sources = composition.Sources.Where(source => source.Id != Template).ToList();
        composition.Steps = composition.Steps.Where(step => step.SourceId != Template).ToList();
        composition.Origins = composition.Origins.Where(origin => origin.Step != 6).ToList();
        composition.Content().Remove("retries");
        return composition;
    }
}
