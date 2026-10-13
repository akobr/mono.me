using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.OpenApi;

using ApiOpenApiOptions = _42.Platform.Storyteller.Api.OpenApi.OpenApiConfigurationOptions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Renders the OpenAPI document the way the Functions host does (the same engine, options and filters), so the SDKs can be
// regenerated without a running host: set STORYTELLER_OPENAPI_EXPORT to a file path and the rendered document is written there.
public class OpenApiExportTests
{
    public const string ExportVariable = "STORYTELLER_OPENAPI_EXPORT";

    // The API assembly to render; by default the one in the API project's own output directory.
    public const string AssemblyVariable = "STORYTELLER_OPENAPI_ASSEMBLY";

    [Fact]
    public async Task Document_HasEveryKeyValueOperation_UnderTheKeyValuesTag()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .ToList();
        var keyValueOperations = operations
            .Where(operation => operation.GetProperty("tags").EnumerateArray().Any(tag => tag.GetString() == Definitions.Tags.KeyValues))
            .Select(operation => operation.GetProperty("operationId").GetString())
            .ToList();
        var expected = typeof(Definitions.RouteIds.KeyValues)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!)
            .ToList();

        keyValueOperations.ShouldBe(expected, ignoreOrder: true);
        document.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("KeyValuePolicy", out _).ShouldBeTrue();
        document.RootElement.GetProperty("info").GetProperty("version").GetString().ShouldMatch(@"^\d+\.\d+\.\d+\.\d+$", "the file version of the API assembly, which names the exported document");
        document.RootElement.GetProperty("servers")[0].GetProperty("url").GetString().ShouldBe("http://localhost:7071/api");

        var exportPath = Environment.GetEnvironmentVariable(ExportVariable);

        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            await File.WriteAllTextAsync(exportPath, json);
        }
    }

    [Fact]
    public async Task Document_DiffOperations_OfferUnifiedText()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var diff = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .Single(operation => operation.GetProperty("operationId").GetString() == Definitions.RouteIds.KeyValues.GetKeyValueVersionDiff);

        diff.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("text/plain", out _).ShouldBeTrue();
    }

    internal static async Task<string> RenderAsync()
    {
        // the same context the extension builds for the host, with the API's own options and filters
        var assembly = LoadApiAssembly();
        var options = new ApiOpenApiOptions();
        var context = new OpenApiHttpTriggerContext(options, new DefaultOpenApiHttpTriggerAuthorization(), new DefaultOpenApiCustomUIOptions(assembly));
        var document = context.Document
            .InitialiseDocument()
            .AddMetadata(options.Info)
            .AddServer(new LocalHostRequest(), "api", options)
            .AddNamingStrategy(options.NamingStrategy)
            .AddVisitors(context.GetVisitorCollection())
            .Build(assembly, options.OpenApiVersion)
            .ApplyDocumentFilters(context.GetDocumentFilterCollection());

        return await document.RenderAsync(OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Json);
    }

    // The extension looks for functions in every assembly next to the one it renders, as the host does in its bin folder.
    // Next to the test copy sit the test platform assemblies, whose types do not load, so the copy in the API project's own
    // output is rendered instead; it is loaded apart, and everything it references resolves to the assemblies already loaded.
    private static Assembly LoadApiAssembly()
    {
        var testCopy = typeof(AccessHttp).Assembly;
        var path = Environment.GetEnvironmentVariable(AssemblyVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            // <project>/test/bin/<configuration>/<framework>/ -> <project>/src/bin/<configuration>/<framework>/
            var testOutput = new DirectoryInfo(Path.GetDirectoryName(testCopy.Location)!);
            var project = testOutput.Parent!.Parent!.Parent!.Parent!;
            path = Path.Combine(project.FullName, "src", "bin", testOutput.Parent.Name, testOutput.Name, Path.GetFileName(testCopy.Location));
        }

        File.Exists(path).ShouldBeTrue($"The API assembly to render was not found at {path}; set {AssemblyVariable}.");
        return new AssemblyLoadContext("storyteller-openapi").LoadFromAssemblyPath(path);
    }

    // The request the host would see from a local developer: http://localhost:7071/api.
    private sealed class LocalHostRequest : IHttpRequestDataObject
    {
        public string Scheme => "http";

        public HostString Host => new("localhost", 7071);

        public IQueryCollection Query => new QueryCollection();

        public IHeaderDictionary Headers => new HeaderDictionary();

        public Stream Body => Stream.Null;
    }
}
