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
        schemas["AccountCreate"].Properties.Keys.ShouldBe(["Organization", "Project", "ProjectDescription"], ignoreOrder: true);
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

    [Fact]
    public void ViewTransferSchemas_ArePublished()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(ViewTransferHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();

        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        schemas.Keys.ShouldContain("ViewImportSummary");
        schemas.Keys.ShouldContain("ViewImportItem");
        schemas.Keys.ShouldNotContain(key => key.StartsWith("Configuration", StringComparison.Ordinal) && (key.Contains("Export") || key.Contains("Import")));
        schemas["ViewExport"].Properties.Keys.ShouldContain("FileName");
        schemas["ViewImport"].Properties.Keys.ShouldContain("Items");
        schemas["ViewImportItem"].Properties["Outcome"].Enum.Count.ShouldBe(5);

        // the include filter: a list of kinds, on the export's body and on both jobs
        ShouldBeAListOfKinds(schemas["ViewExportCreate"].Properties["Include"]);
        ShouldBeAListOfKinds(schemas["ViewExport"].Properties["Include"]);
        ShouldBeAListOfKinds(schemas["ViewImport"].Properties["Include"]);
        schemas["ViewExportCreate"].Properties["Format"].Enum.Select(Name).ShouldBe(["Folder", "Git"]);

        schemas["ViewImport"].Properties["Existing"].Enum.Select(Name).ShouldBe(["Overwrite", "Skip"]);
        schemas["ViewImport"].Properties["Summaries"].Type.ShouldBe("array");
        schemas["ViewImport"].Properties["Summaries"].Items.Reference.Id.ShouldBe("ViewImportSummary");
        schemas["ViewImportSummary"].Properties["Content"].Enum.Select(Name).ShouldBe(["Annotations", "Schemas", "Templates", "Configurations"]);
        schemas["ViewImportSummary"].Properties.Keys.ShouldContain("Warnings");
        schemas["ViewImportSummary"].Properties.Keys.ShouldContain("ExcludedFiles");

        schemas["ViewImportItem"].Properties["Kind"].Enum.Select(Name).ShouldBe(["Annotation", "TypeSchema", "DescendantTypeSchema", "AnnotationSchema", "Template", "Configuration"]);
        schemas["ViewImportItem"].Properties.Keys.ShouldContain("AnnotationType");
        schemas["ViewImportItem"].Properties["Warnings"].Type.ShouldBe("array");
        schemas["ViewImportItem"].Properties["Warnings"].Items.Reference.Id.ShouldBe("ViewImportWarning");
        schemas["ViewImportWarning"].Properties.Keys.ShouldBe(["Code", "Message"], ignoreOrder: true);

        schemas["ViewExport"].Properties.Keys
            .Where(key => key.EndsWith("Count", StringComparison.Ordinal))
            .ShouldBe(["AnnotationCount", "SchemaCount", "TemplateCount", "ConfigurationCount", "VersionCount"], ignoreOrder: true);
    }

    // The model is flat: no schema refers to itself. The enums of a source are written inline, as ViewImportItem.Kind is.
    [Fact]
    public void ConfigurationCompositionSchemas_ArePublished()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(ConfigurationHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();

        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        schemas["ConfigurationComposition"].Properties.Keys.ShouldBe(["AnnotationKey", "Content", "Hash", "Sources", "Nodes", "Steps", "Origins"], ignoreOrder: true);
        schemas["ConfigurationCompositionSource"].Properties.Keys.ShouldBe(["Id", "Kind", "AnnotationType", "AnnotationKey", "Presence", "Version", "Modified", "Content"], ignoreOrder: true);
        schemas["ConfigurationCompositionNode"].Properties.Keys.ShouldBe(["Index", "AnnotationKey", "MergedInto", "Depth"], ignoreOrder: true);
        schemas["ConfigurationCompositionStep"].Properties.Keys.ShouldBe(["Order", "SourceId", "Node"], ignoreOrder: true);
        schemas["ConfigurationCompositionOrigin"].Properties.Keys.ShouldBe(["Path", "Step"], ignoreOrder: true);

        var composition = schemas["ConfigurationComposition"];
        composition.Properties["Content"].Type.ShouldBe("object");
        composition.Properties["Sources"].Items.Reference.Id.ShouldBe("ConfigurationCompositionSource");
        composition.Properties["Nodes"].Items.Reference.Id.ShouldBe("ConfigurationCompositionNode");
        composition.Properties["Steps"].Items.Reference.Id.ShouldBe("ConfigurationCompositionStep");
        composition.Properties["Origins"].Items.Reference.Id.ShouldBe("ConfigurationCompositionOrigin");

        var source = schemas["ConfigurationCompositionSource"];
        source.Properties["Kind"].Enum.Select(Name).ShouldBe(["Configuration", "Template"]);
        source.Properties["Presence"].Enum.Select(Name).ShouldBe(["Missing", "Empty", "Present"]);
        source.Properties["AnnotationType"].Enum.Select(Name).ShouldBe(["Responsibility", "Unit", "Subject", "Usage", "Context", "Execution", "UnitOfExecution"]);
        source.Properties["Modified"].Reference.Id.ShouldBe("AuditStamp");
        schemas.Keys.ShouldNotContain(key => key.StartsWith("ConfigurationCompositionSource", StringComparison.Ordinal) && key != "ConfigurationCompositionSource");

        var mergedInto = schemas["ConfigurationCompositionNode"].Properties["MergedInto"];
        mergedInto.Type.ShouldBe("integer");
        mergedInto.Nullable.ShouldBeTrue();
        schemas["ConfigurationCompositionNode"].Properties["Depth"].Nullable.ShouldBeFalse();
    }

    private static void ShouldBeAListOfKinds(OpenApiSchema include)
    {
        include.Type.ShouldBe("array");
        include.Items.Enum.Select(Name).ShouldBe(["Annotations", "Schemas", "Templates", "Configurations"]);
    }

    private static string Name(Microsoft.OpenApi.Any.IOpenApiAny value)
    {
        return ((Microsoft.OpenApi.Any.OpenApiString)value).Value;
    }
}
