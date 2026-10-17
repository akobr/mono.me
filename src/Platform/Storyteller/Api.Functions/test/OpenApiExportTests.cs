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

    // The export and import of a view: five operations below the view, the include filter as a comma-separated list of
    // kinds, and no route of the first spec below configurations/.
    [Fact]
    public async Task Document_HasTheViewTransferOperations_UnderTheTransfersTag()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToList();
        var operations = paths
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (Path: path.Name, Method: operation.Name, Operation: operation.Value)))
            .Where(operation => operation.Operation.GetProperty("tags").EnumerateArray().Any(tag => tag.GetString() == Definitions.Tags.Transfers))
            .ToList();

        operations.Select(operation => $"{operation.Method.ToUpperInvariant()} {operation.Path} {operation.Operation.GetProperty("operationId").GetString()}").ShouldBe(
            [
                "POST /v1/{organization}/{project}/{view}/exports StartViewExport",
                "GET /v1/{organization}/{project}/{view}/exports/{id} GetViewExport",
                "GET /v1/{organization}/{project}/{view}/exports/{id}/archive GetViewExportArchive",
                "POST /v1/{organization}/{project}/{view}/imports StartViewImport",
                "GET /v1/{organization}/{project}/{view}/imports/{id} GetViewImport",
            ],
            ignoreOrder: true);
        paths.Select(path => path.Name).ShouldNotContain(path => path.Contains("/configurations/exports", StringComparison.Ordinal) || path.Contains("/configurations/imports", StringComparison.Ordinal));
        operations.Where(operation => !operation.Operation.GetProperty("responses").TryGetProperty("403", out _)).ShouldBeEmpty("every operation documents its 403");

        var import = operations.Single(operation => operation.Path.EndsWith("/imports", StringComparison.Ordinal)).Operation;
        var parameters = import.GetProperty("parameters").EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        parameters.Keys.ShouldBe(["include", "existing", "force", "dryRun", "fileName"], ignoreOrder: true);

        var include = parameters["include"];
        include.GetProperty("style").GetString().ShouldBe("form");
        include.GetProperty("explode").GetBoolean().ShouldBeFalse("comma-separated");
        include.GetProperty("schema").GetProperty("type").GetString().ShouldBe("array");
        include.GetProperty("schema").GetProperty("items").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ViewArchiveContent");
        parameters["existing"].GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ViewImportExisting");

        // the kinds and the existing option are shared schemas, so both SDKs have one type for each
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var content = schemas.GetProperty("ViewArchiveContent");
        content.GetProperty("enum").EnumerateArray().Select(kind => kind.GetString()).ShouldBe(["Annotations", "Schemas", "Templates", "Configurations"]);
        content.TryGetProperty("default", out _).ShouldBeFalse("no include means all four kinds, not the first");
        var existing = schemas.GetProperty("ViewImportExisting");
        existing.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ShouldBe(["Overwrite", "Skip"]);
        existing.GetProperty("default").GetString().ShouldBe("Overwrite");

        foreach (var name in new[] { "ViewExportCreate", "ViewExport", "ViewImport" })
        {
            schemas.GetProperty(name).GetProperty("properties").GetProperty("Include").GetProperty("items").GetProperty("$ref").GetString()
                .ShouldBe("#/components/schemas/ViewArchiveContent", name);
        }

        schemas.GetProperty("ViewImport").GetProperty("properties").GetProperty("Existing").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/ViewImportExisting");
    }

    // The token endpoint speaks OAuth: a form body and snake_case JSON, named the same in the document and on the wire.
    [Fact]
    public async Task Document_TokenExchange_UsesTheOAuthFormAndSnakeCaseModels()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        PropertyNames(schemas, "MachineTokenRequest").ShouldBe(
            ["grant_type", "subject_token", "subject_token_type", "client_id", "audience", "resource", "scope", "requested_token_type"],
            ignoreOrder: true);
        RequiredNames(schemas, "MachineTokenRequest").ShouldBe(["grant_type", "subject_token", "subject_token_type", "client_id"], ignoreOrder: true);
        PropertyNames(schemas, "MachineTokenResponse").ShouldBe(["access_token", "issued_token_type", "token_type", "expires_in", "scope"], ignoreOrder: true);
        PropertyNames(schemas, "OAuthErrorResponse").ShouldBe(["error", "error_description"], ignoreOrder: true);

        var exchange = Operation(document, Definitions.RouteIds.Access.ExchangeMachineToken);
        exchange.GetProperty("requestBody").GetProperty("content").EnumerateObject().Select(content => content.Name)
            .ShouldBe([Definitions.ContentTypes.FormUrlEncoded]);
        exchange.GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty(Definitions.ContentTypes.Json)
            .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/OAuthErrorResponse");
        exchange.GetProperty("responses").TryGetProperty("503", out _).ShouldBeTrue();

        // Anonymous: no security requirement and no 403; the management operations have both.
        (exchange.TryGetProperty("security", out var security) && security.GetArrayLength() > 0).ShouldBeFalse();
        exchange.GetProperty("responses").TryGetProperty("403", out _).ShouldBeFalse();

        foreach (var routeId in new[]
                 {
                     Definitions.RouteIds.Access.GetFederatedIssuers,
                     Definitions.RouteIds.Access.GetFederatedIssuer,
                     Definitions.RouteIds.Access.SetFederatedIssuer,
                     Definitions.RouteIds.Access.DeleteFederatedIssuer,
                     Definitions.RouteIds.Access.SetMachineFederationRules,
                 })
        {
            var operation = Operation(document, routeId);
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe([Definitions.Tags.Access]);
            operation.GetProperty("security").GetArrayLength().ShouldBeGreaterThan(0);
            operation.GetProperty("responses").TryGetProperty("403", out _).ShouldBeTrue();
        }

        // Both PUTs can answer 409 (a re-created issuer name in use, a machine changed concurrently), so the SDKs model it.
        foreach (var routeId in new[] { Definitions.RouteIds.Access.SetFederatedIssuer, Definitions.RouteIds.Access.SetMachineFederationRules })
        {
            Operation(document, routeId).GetProperty("responses").GetProperty("409").GetProperty("content").GetProperty(Definitions.ContentTypes.Json)
                .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ErrorResponse");
        }

        PropertyNames(schemas, "FederatedIssuer").ShouldContain("RequiredClaims");
        PropertyNames(schemas, "MachineAccess").ShouldContain("FederationRules");
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

    private static List<string> PropertyNames(JsonElement schemas, string schema)
    {
        return schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(property => property.Name).ToList();
    }

    private static List<string?> RequiredNames(JsonElement schemas, string schema)
    {
        return schemas.GetProperty(schema).GetProperty("required").EnumerateArray().Select(name => name.GetString()).ToList();
    }

    private static JsonElement Operation(JsonDocument document, string operationId)
    {
        return document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .Single(operation => operation.GetProperty("operationId").GetString() == operationId);
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
