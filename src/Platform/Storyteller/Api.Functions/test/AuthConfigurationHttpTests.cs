using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Api.V1.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class AuthConfigurationHttpTests
{
    [Fact]
    public void Describe_EntraId_ReturnsTenantClientAndTheCliScopes()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.EntraId,
            TenantId = "common",
            ClientId = "client-id",
        });

        configuration.Provider.ShouldBe("EntraId");
        configuration.ClientId.ShouldBe("client-id");
        configuration.TenantId.ShouldBe("common");
        configuration.Scopes.ShouldBe(["api://client-id/User.Impersonation"]);
        configuration.AuthKitDomain.ShouldBeNull();
        configuration.Issuer.ShouldBeNull();
    }

    [Fact]
    public void Describe_Keycloak_ReturnsTheRealmIssuerAndTheClientOnly()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            TenantId = "left-over-entra-tenant",
            ClientId = "left-over-entra-client",
            AuthKit = new AuthKitOptions { ClientId = "client_123", AuthKitDomain = "https://example.authkit.app" },
            Keycloak = new KeycloakUserOptions
            {
                Issuer = "https://keycloak.example/realms/storyteller/",
                Audience = "storyteller-api",
                ClientId = "sform",
            },
        });

        configuration.Provider.ShouldBe("Keycloak");
        configuration.ClientId.ShouldBe("sform");
        configuration.Issuer.ShouldBe("https://keycloak.example/realms/storyteller");
        configuration.TenantId.ShouldBeNull();
        configuration.Scopes.ShouldBeNull();
        configuration.AuthKitDomain.ShouldBeNull();
    }

    [Fact]
    public void Describe_KeycloakWithoutAClient_HasAnEmptyClientId()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions { Issuer = "https://keycloak.example/realms/storyteller", Audience = "storyteller-api" },
        });

        configuration.ClientId.ShouldBeEmpty();
        configuration.Issuer.ShouldBe("https://keycloak.example/realms/storyteller");
    }

    [Fact]
    public void Describe_AuthKit_ReturnsClientAndDomainOnly()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            TenantId = "left-over-entra-tenant",
            ClientId = "left-over-entra-client",
            AuthKit = new AuthKitOptions
            {
                ClientId = "client_123",
                AuthKitDomain = "https://example.authkit.app/",
            },
        });

        configuration.Provider.ShouldBe("AuthKit");
        configuration.ClientId.ShouldBe("client_123");
        configuration.AuthKitDomain.ShouldBe("https://example.authkit.app");
        configuration.TenantId.ShouldBeNull();
        configuration.Scopes.ShouldBeNull();
    }

    [Fact]
    public void Describe_AuthKitWithoutDomain_OmitsTheDomain()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions { ClientId = "client_123" },
        });

        configuration.AuthKitDomain.ShouldBeNull();
    }

    [Fact]
    public void Describe_NeverExposesSecrets()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = "client_123",
                ApiKey = "sk_test_secret",
                AuthKitDomain = "https://example.authkit.app",
                MachineOrganizationId = "org_machines",
                PermissionMap = new Dictionary<string, string> { ["storyteller:admin"] = "Secrets.Read" },
            },
        });

        var json = JsonSerializer.Serialize(configuration);

        json.ShouldNotContain("sk_test_secret");
        json.ShouldNotContain("org_machines");
        json.ShouldNotContain("storyteller:admin");
    }

    [Fact]
    public void Describe_Keycloak_NeverExposesTheRoleMapOrTheAllowedClients()
    {
        var configuration = AuthConfigurationHttp.Describe(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions
            {
                Issuer = "https://keycloak.example/realms/storyteller",
                Audience = "storyteller-api",
                AllowedClientIds = ["internal-tool"],
                RoleMap = new Dictionary<string, string> { ["storyteller-api:admin"] = "Annotation.Write" },
            },
        });

        var json = JsonSerializer.Serialize(configuration);

        json.ShouldNotContain("internal-tool");
        json.ShouldNotContain("storyteller-api");
    }

    // Spec 5: clients see whether actors may exchange user tokens, for every provider; off without the settings.
    [Theory]
    [InlineData(IdentityProviderKind.EntraId)]
    [InlineData(IdentityProviderKind.AuthKit)]
    [InlineData(IdentityProviderKind.Keycloak)]
    public void Describe_TellsWhetherTheTokenExchangeIsEnabled(IdentityProviderKind provider)
    {
        var options = new UserAuthenticationOptions
        {
            Provider = provider,
            ClientId = "client-id",
            AuthKit = new AuthKitOptions { ClientId = "client_123" },
            Keycloak = new KeycloakUserOptions { Issuer = "https://keycloak.example/realms/storyteller", Audience = "storyteller-api" },
        };

        AuthConfigurationHttp.Describe(options).TokenExchange.Enabled.ShouldBeFalse();
        AuthConfigurationHttp.Describe(options, new TokenExchangeOptions()).TokenExchange.Enabled.ShouldBeFalse();
        AuthConfigurationHttp.Describe(options, new TokenExchangeOptions { Enabled = true }).TokenExchange.Enabled.ShouldBeTrue();
    }

    // Only the flag: who may act for users stays in the configuration.
    [Fact]
    public void Describe_NeverExposesTheActorsOrTheAdditionalIssuers()
    {
        var configuration = AuthConfigurationHttp.Describe(
            new UserAuthenticationOptions { Provider = IdentityProviderKind.AuthKit, AuthKit = new AuthKitOptions { ClientId = "client_123" } },
            new TokenExchangeOptions
            {
                Enabled = true,
                AdditionalIssuers = [new TokenExchangeIssuerOptions { Issuer = "https://auth.example.com", JwksUri = "https://auth.example.com/oauth2/jwks" }],
                Actors =
                [
                    new TokenExchangeActorOptions
                    {
                        Id = "mcp-actor",
                        Authentication = new ActorAuthenticationOptions { ClientSecretHash = new string('a', 64) },
                        SubjectAudiences = ["https://mcp.example/mcp"],
                        AllowedClientIds = ["client_mcp"],
                    },
                ],
            });

        var json = JsonSerializer.Serialize(configuration);

        json.ShouldContain("\"TokenExchange\":{\"Enabled\":true}");
        json.ShouldNotContain("mcp-actor");
        json.ShouldNotContain("aaaaaaaa");
        json.ShouldNotContain("mcp.example");
        json.ShouldNotContain("client_mcp");
        json.ShouldNotContain("auth.example.com");
    }

    [Fact]
    public void GetAuthConfiguration_ReportsTheTokenExchangeOfItsOptions()
    {
        var function = new AuthConfigurationHttp(
            Options.Create(new UserAuthenticationOptions { Provider = IdentityProviderKind.EntraId, ClientId = "client-id" }),
            Options.Create(new TokenExchangeOptions { Enabled = true }));
        using var services = new ServiceCollection().BuildServiceProvider();
        var (_, request) = FunctionTestDoubles.CreateRequest(services);

        var result = function.GetAuthConfiguration(request);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AuthConfiguration>().TokenExchange.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void GetAuthConfiguration_ReturnsOkWithTheDescription()
    {
        var function = new AuthConfigurationHttp(Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions { ClientId = "client_123" },
        }));
        using var services = new ServiceCollection().BuildServiceProvider();
        var (_, request) = FunctionTestDoubles.CreateRequest(services);

        var result = function.GetAuthConfiguration(request);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        ok.Value.ShouldBeOfType<AuthConfiguration>().ClientId.ShouldBe("client_123");
    }
}
