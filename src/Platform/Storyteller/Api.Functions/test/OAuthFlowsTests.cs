using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.OpenApi.Filters;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Visitors;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class OAuthFlowsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("EntraId")]
    public void EntraId_KeepsTheImplicitAndClientCredentialsFlows(string? provider)
    {
        var flows = Create(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = provider,
            ["Auth:TenantId"] = "tenant-id",
            ["Auth:ClientId"] = "client-id",
        });

        flows.Implicit.ShouldNotBeNull();
        flows.Implicit.AuthorizationUrl.ShouldBe(new Uri("https://login.microsoftonline.com/tenant-id/oauth2/v2.0/authorize"));
        flows.Implicit.Scopes.Keys.ShouldContain("api://client-id/User.Impersonation");
        flows.Implicit.Scopes.Count.ShouldBe(8);
        flows.ClientCredentials.ShouldNotBeNull();
        flows.ClientCredentials.TokenUrl.ShouldBe(new Uri("https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token"));
        flows.ClientCredentials.Scopes.Keys.ShouldBe(["api://client-id/.default"]);
        flows.AuthorizationCode.ShouldBeNull();
    }

    [Fact]
    public void AuthKit_WithDomain_DescribesAuthorizationCodeAndClientCredentials()
    {
        var flows = Create(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "authkit",
            ["Auth:TenantId"] = "left-over-tenant",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:AuthKitDomain"] = "https://example.authkit.app/",
        });

        flows.Implicit.ShouldBeNull();
        flows.AuthorizationCode.ShouldNotBeNull();
        flows.AuthorizationCode.AuthorizationUrl.ShouldBe(new Uri("https://example.authkit.app/oauth2/authorize"));
        flows.AuthorizationCode.TokenUrl.ShouldBe(new Uri("https://example.authkit.app/oauth2/token"));
        flows.AuthorizationCode.Scopes.Keys.ShouldBe(["openid", "profile", "email"], ignoreOrder: true);
        flows.ClientCredentials.ShouldNotBeNull();
        flows.ClientCredentials.TokenUrl.ShouldBe(new Uri("https://example.authkit.app/oauth2/token"));
    }

    [Fact]
    public void AuthKit_WithoutDomain_HasOnlyTheMarkedStandIn()
    {
        var flows = Create(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = "client_123",
        });

        OAuthFlows.IsPlaceholder(flows).ShouldBeTrue();
        flows.Implicit.ShouldBeNull();
        flows.AuthorizationCode.ShouldBeNull();
        flows.Password.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.authkit.app")]
    public void EntraIdOrAuthKitWithDomain_IsNotAPlaceholder(string? authKitDomain)
    {
        var flows = Create(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = authKitDomain is null ? "EntraId" : "AuthKit",
            ["Auth:AuthKit:AuthKitDomain"] = authKitDomain,
        });

        OAuthFlows.IsPlaceholder(flows).ShouldBeFalse();
    }

    [Fact]
    public void Filter_SchemeWithoutFlows_IsRemovedWithItsRequirements()
    {
        var document = CreateDocument(new OpenApiOAuthFlows());

        new OAuthSecuritySchemeDocumentFilter().Apply(null!, document);

        document.Components.SecuritySchemes.Keys.ShouldBe(["manual"]);
        var operation = document.Paths["/v1/access/account"].Operations[OperationType.Get];
        operation.Security.Count.ShouldBe(1);
        operation.Security[0].Keys.Single().Reference.Id.ShouldBe("manual");
    }

    [Fact]
    public void Filter_SchemeWithFlows_IsKept()
    {
        var document = CreateDocument(Create(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:AuthKitDomain"] = "https://example.authkit.app",
        }));

        new OAuthSecuritySchemeDocumentFilter().Apply(null!, document);

        document.Components.SecuritySchemes.Keys.ShouldBe(["manual", "integrated"], ignoreOrder: true);
        document.Paths["/v1/access/account"].Operations[OperationType.Get].Security.Count.ShouldBe(2);
    }

    [Fact]
    public void Extension_ReferencesTheIntegratedSchemeById()
    {
        // The filter matches requirement keys by reference id. Pin that to what the extension emits.
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var method = typeof(AccessHttp).GetMethod(nameof(AccessHttp.GetAccount))!;

        var requirements = helper.GetOpenApiSecurityRequirement(method, new DefaultNamingStrategy());

        var integrated = requirements.SelectMany(requirement => requirement.Keys).Single(scheme => scheme.Type == SecuritySchemeType.OAuth2);
        integrated.Reference.Id.ShouldBe("integrated");
        integrated.Flows.ShouldBeOfType<OAuthFlows>();
    }

    [Fact]
    public void Extension_AuthKitWithoutDomain_BuildsAndTheFilterLeavesOnlyTheBearerScheme()
    {
        // The extension builds every requirement before document filters run, and it throws
        // "Flow MUST be provided" for an OAuth2 scheme without a flow.
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var method = typeof(SampleFunctions).GetMethod(nameof(SampleFunctions.Operation))!;
        var namingStrategy = new DefaultNamingStrategy();

        var requirements = helper.GetOpenApiSecurityRequirement(method, namingStrategy);
        var schemes = helper.GetOpenApiSecuritySchemes([method], namingStrategy);

        var operation = new OpenApiOperation { Security = requirements };
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/sample"] = new OpenApiPathItem { Operations = { [OperationType.Get] = operation } },
            },
            Components = new OpenApiComponents { SecuritySchemes = schemes },
        };

        new OAuthSecuritySchemeDocumentFilter().Apply(null!, document);

        document.Components.SecuritySchemes.Keys.ShouldBe(["manual"]);
        operation.Security.SelectMany(requirement => requirement.Keys).Select(scheme => scheme.Reference.Id).ShouldBe(["manual"]);
    }

    private static OAuthFlows Create(Dictionary<string, string?> values)
    {
        return new OAuthFlows(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    // The shape the OpenAPI extension produces for [OpenApiSecurity] attributes: one requirement per attribute.
    private static OpenApiDocument CreateDocument(OpenApiOAuthFlows flows)
    {
        var manual = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "manual" },
        };
        var integrated = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = flows,
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "integrated" },
        };

        var operation = new OpenApiOperation
        {
            Security =
            [
                new OpenApiSecurityRequirement { [manual] = [] },
                new OpenApiSecurityRequirement { [integrated] = [] },
            ],
        };

        return new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/v1/access/account"] = new OpenApiPathItem
                {
                    Operations = { [OperationType.Get] = operation },
                },
            },
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    ["manual"] = manual,
                    ["integrated"] = integrated,
                },
            },
        };
    }

    // Created by the extension with Activator, like OAuthFlows itself.
    internal sealed class AuthKitWithoutDomainFlows : OAuthFlows
    {
        public AuthKitWithoutDomainFlows()
            : base(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Provider"] = "AuthKit",
                    ["Auth:AuthKit:ClientId"] = "client_123",
                })
                .Build())
        {
        }
    }

    internal static class SampleFunctions
    {
        [OpenApiSecurity("manual", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiSecurity("integrated", SecuritySchemeType.OAuth2, Flows = typeof(AuthKitWithoutDomainFlows))]
        public static void Operation()
        {
        }
    }
}
