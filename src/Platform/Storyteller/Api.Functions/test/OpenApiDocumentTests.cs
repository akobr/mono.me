using System.Reflection;

using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Visitors;

using Newtonsoft.Json.Serialization;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class OpenApiDocumentTests
{
    [Fact]
    public void Schemas_ForTheFunctionsAssembly_IncludeAccountCreateOnce()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name.StartsWith("OpenApi", StringComparison.Ordinal)))
            .ToList();

        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        schemas.Keys.Count(key => key == "AccountCreate").ShouldBe(1);
        schemas["AccountCreate"].Properties.Keys.ShouldBe(["Organization", "Project"], ignoreOrder: true);
    }
}
