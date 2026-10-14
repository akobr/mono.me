using System.Reflection;

using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Visitors;

using Microsoft.OpenApi.Models;

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

    [Fact]
    public void AnnotationSchema_PublishesTheMaximalLengths()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(AnnotationsHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();

        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        var annotation = schemas["Annotation"];
        annotation.Properties["Title"].MaxLength.ShouldBe(AnnotationLimits.TitleMaxLength);
        annotation.Properties["Description"].MaxLength.ShouldBe(AnnotationLimits.DescriptionMaxLength);
        annotation.Properties["DocumentationLink"].MaxLength.ShouldBe(AnnotationLimits.DocumentationLinkMaxLength);
        schemas.Keys.ShouldContain("AnnotationHistory");
        schemas.Keys.ShouldContain("AnnotationRestoreResult");
        schemas.Keys.ShouldContain("AnnotationDeletionResult");
    }

    [Fact]
    public void BindingCatalogSchema_PublishesFunctionsAndSources()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(BindingsHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();

        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        schemas["BindingCatalog"].Properties.Keys.ShouldBe(["Functions", "Sources"], ignoreOrder: true);
        schemas["BindingFunctionDescriptor"].Properties.Keys.ShouldBe(["Name", "Summary", "Signature", "Parameters", "Examples"], ignoreOrder: true);
        schemas["BindingSourceDescriptor"].Properties.Keys.ShouldBe(["Name", "Summary", "IsDefault", "IsSecret", "Examples"], ignoreOrder: true);
        schemas["BindingParameterDescription"].Properties.Keys.ShouldBe(["Name", "Description", "IsOptional", "Values"], ignoreOrder: true);
        schemas["BindingExample"].Properties.Keys.ShouldBe(["Expression", "Description"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(nameof(AnnotationsHttp.GetAnnotations))]
    [InlineData(nameof(AnnotationsHttp.GetAnnotation))]
    [InlineData(nameof(AnnotationsHttp.GetDescendants))]
    [InlineData(nameof(AnnotationsHttp.GetResponsibilities))]
    [InlineData(nameof(AnnotationsHttp.GetSubjects))]
    [InlineData(nameof(AnnotationsHttp.GetUsages))]
    [InlineData(nameof(AnnotationsHttp.GetContexts))]
    [InlineData(nameof(AnnotationsHttp.GetExecutions))]
    [InlineData(nameof(AnnotationsHttp.GetUnits))]
    [InlineData(nameof(AnnotationsHttp.GetUnitsOfExecution))]
    public void AnnotationReads_PublishTheIncludeFlags(string operation)
    {
        var method = typeof(AnnotationsHttp).GetMethod(operation).ShouldNotBeNull();
        var published = method.GetCustomAttributes<OpenApiParameterAttribute>().ToDictionary(parameter => parameter.Name);

        foreach (var name in new[] { "includeInactive", "includeDeleted" })
        {
            var parameter = published[name];
            parameter.In.ShouldBe(ParameterLocation.Query);
            parameter.Type.ShouldBe(typeof(bool));
            parameter.Required.ShouldBeFalse();
            method.GetParameters().Single(argument => argument.Name == name).DefaultValue.ShouldBe(false);
        }
    }
}
