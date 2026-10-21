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

public class MachineScopeDocumentFilterTests
{
    [Fact]
    public void Apply_ScopesReferenceTheSharedScopeSchemaWithoutADefault()
    {
        var document = Document();
        var schemas = document.Components.Schemas;
        schemas["MachineAccessCreate"].Properties["Scopes"].Items.Reference.ShouldBeNull("the generator inlines the enum");

        new MachineScopeDocumentFilter().Apply(null!, document);

        foreach (var name in new[] { "MachineAccess", "MachineAccessCreate" })
        {
            var items = schemas[name].Properties["Scopes"].Items;
            items.Reference.Id.ShouldBe("MachineScope", name);
            items.Default.ShouldBeNull(name);
        }

        var scope = schemas["MachineScope"];
        scope.Type.ShouldBe("string");
        scope.Default.ShouldBeNull("a scope is only ever granted on purpose");
        scope.Enum.Cast<OpenApiString>().Select(value => value.Value)
            .ShouldBe(
            [
                "AnnotationRead",
                "AnnotationWrite",
                "ConfigurationRead",
                "ConfigurationWrite",
                "SecretsRead",
                "SecretsWrite",
                "ConfigurationTransfer",
                "SchemaWrite",
                "TemplateWrite",
                "AnnotationManage",
                "KeyValueManage",
            ]);
    }

    [Fact]
    public void Apply_ADocumentWithoutTheMachineSchemas_GetsNoScopeSchema()
    {
        var document = new OpenApiDocument { Components = new OpenApiComponents { Schemas = new Dictionary<string, OpenApiSchema>() } };

        new MachineScopeDocumentFilter().Apply(null!, document);

        document.Components.Schemas.ShouldBeEmpty();
    }

    private static OpenApiDocument Document()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(AccessHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();
        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        return new OpenApiDocument { Components = new OpenApiComponents { Schemas = schemas } };
    }
}
