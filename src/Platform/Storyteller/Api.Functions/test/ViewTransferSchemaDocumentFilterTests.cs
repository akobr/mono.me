using System.Reflection;

using _42.Platform.Storyteller.Api.OpenApi.Filters;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Visitors;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

using Newtonsoft.Json.Serialization;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ViewTransferSchemaDocumentFilterTests
{
    private static readonly string[] Kinds = ["Annotations", "Schemas", "Templates", "Configurations"];

    [Fact]
    public void Apply_ListsOfKindsReferenceTheSharedKindSchema()
    {
        var document = Document();
        var schemas = document.Components.Schemas;
        schemas["ViewExportCreate"].Properties["Include"].Items.Default.ShouldNotBeNull("the generator's default, which the filter removes");

        new ViewTransferSchemaDocumentFilter().Apply(null!, document);

        foreach (var name in new[] { "ViewExportCreate", "ViewExport", "ViewImport" })
        {
            var include = schemas[name].Properties["Include"];
            include.Type.ShouldBe("array", name);
            include.Items.Reference.Id.ShouldBe("ViewArchiveContent", name);
        }

        Names(schemas["ViewArchiveContent"].Enum).ShouldBe(Kinds);
        schemas["ViewArchiveContent"].Default.ShouldBeNull("no include means all four kinds");
        schemas["ViewImportSummary"].Properties["Content"].Reference.ShouldBeNull("a nullable kind stays inline");
    }

    [Fact]
    public void Apply_TheExistingOptionOfTheJobReferencesTheSharedSchema()
    {
        var document = Document();

        new ViewTransferSchemaDocumentFilter().Apply(null!, document);

        var schemas = document.Components.Schemas;
        schemas["ViewImport"].Properties["Existing"].Reference.Id.ShouldBe("ViewImportExisting");
        Names(schemas["ViewImportExisting"].Enum).ShouldBe(["Overwrite", "Skip"]);
        ((OpenApiString)schemas["ViewImportExisting"].Default).Value.ShouldBe("Overwrite");
    }

    [Fact]
    public void Apply_TheQueryOfTheImportReferencesTheSharedSchemas()
    {
        var document = Document();
        var include = new OpenApiParameter
        {
            Name = Definitions.Parameters.Include,
            In = ParameterLocation.Query,
            Style = ParameterStyle.Form,
            Explode = false,
            Schema = new OpenApiSchema { Type = "array", Items = InlineEnum(Kinds) },
        };
        var existing = new OpenApiParameter { Name = Definitions.Parameters.Existing, In = ParameterLocation.Query, Schema = InlineEnum("Overwrite", "Skip") };
        var otherInclude = new OpenApiParameter { Name = Definitions.Parameters.Include, In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "array", Items = InlineEnum("a", "b") } };
        document.Paths = new OpenApiPaths
        {
            ["/v1/{organization}/{project}/{view}/imports"] = new OpenApiPathItem
            {
                Operations =
                {
                    [OperationType.Post] = new OpenApiOperation { OperationId = Definitions.RouteIds.Transfers.StartViewImport, Parameters = [include, existing] },
                },
            },
            ["/v1/other"] = new OpenApiPathItem
            {
                Operations =
                {
                    [OperationType.Get] = new OpenApiOperation { OperationId = "Other", Parameters = [otherInclude] },
                },
            },
        };

        new ViewTransferSchemaDocumentFilter().Apply(null!, document);

        include.Schema.Type.ShouldBe("array");
        include.Schema.Items.Reference.Id.ShouldBe("ViewArchiveContent");
        include.Explode.ShouldBeFalse("still comma-separated");
        existing.Schema.Reference.Id.ShouldBe("ViewImportExisting");
        otherInclude.Schema.Items.Reference.ShouldBeNull("only the import's parameters are touched");
    }

    [Fact]
    public void Apply_ADocumentWithoutTransfers_GetsNoSharedSchemas()
    {
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents { Schemas = new Dictionary<string, OpenApiSchema> { ["Account"] = new() { Type = "object" } } },
        };

        new ViewTransferSchemaDocumentFilter().Apply(null!, document);

        document.Components.Schemas.Keys.ShouldBe(["Account"]);
    }

    private static OpenApiDocument Document()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(ViewTransferHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();
        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        return new OpenApiDocument { Components = new OpenApiComponents { Schemas = schemas } };
    }

    private static OpenApiSchema InlineEnum(params string[] values) => new()
    {
        Type = "string",
        Enum = values.Select(IOpenApiAny (value) => new OpenApiString(value)).ToList(),
        Default = new OpenApiString(values[0]),
    };

    private static IEnumerable<string> Names(IEnumerable<IOpenApiAny> values)
    {
        return values.Cast<OpenApiString>().Select(value => value.Value);
    }
}
