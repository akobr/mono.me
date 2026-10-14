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

public class MachineCredentialKindDocumentFilterTests
{
    [Fact]
    public void Apply_RemovesTheDefaultOfTheNullableKinds()
    {
        var document = Document();
        var schemas = document.Components.Schemas;
        schemas["MachineAccessCreate"].Properties["CredentialKind"].Default.ShouldNotBeNull();

        new MachineCredentialKindDocumentFilter().Apply(null!, document);

        schemas["MachineAccessCreate"].Properties["CredentialKind"].Default.ShouldBeNull();
        schemas["MachineAccessCreate"].Properties["CredentialKind"].Nullable.ShouldBeTrue();
        schemas["MachineAuthenticationPolicy"].Properties["CredentialKind"].Default.ShouldBeNull();
        schemas["MachineAccess"].Properties["CredentialKind"].Default.ShouldNotBeNull();
    }

    [Fact]
    public void Apply_EnabledKindsReferenceTheSharedKindSchema()
    {
        var document = Document();

        new MachineCredentialKindDocumentFilter().Apply(null!, document);

        var schemas = document.Components.Schemas;
        schemas["MachineAuthenticationPolicy"].Properties["EnabledCredentialKinds"].Items.Reference.Id.ShouldBe("MachineCredentialKind");
        schemas["MachineCredentialKind"].Enum.Cast<OpenApiString>().Select(value => value.Value)
            .ShouldBe(["ApiKey", "Certificate", "CertificateAndApiKey", "ClientCredentials"]);
    }

    private static OpenApiDocument Document()
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var methods = typeof(AccessHttp).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();
        var schemas = helper.GetOpenApiSchemas(methods, new DefaultNamingStrategy(), VisitorCollection.CreateInstance());

        return new OpenApiDocument { Components = new OpenApiComponents { Schemas = schemas } };
    }
}
