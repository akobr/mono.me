using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class UserAuthenticationOptionsTests
{
    private const string McpClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11";
    private const string McpTenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";

    private readonly UserAuthenticationOptionsValidator _validator = new();

    [Fact]
    public void Bind_MissingProvider_DefaultsToEntraId()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auth:TenantId"] = "tenant",
            ["Auth:ClientId"] = "client",
        });

        options.Provider.ShouldBe(IdentityProviderKind.EntraId);
        options.AuthKit.Issuer.ShouldBe("https://api.workos.com/");
        options.AuthKit.ApiBaseUrl.ShouldBe("https://api.workos.com");
        options.AuthKit.ApiKeyScheme.ShouldBe("WorkOS");
        options.AuthKit.Audience.ShouldBeNull();
    }

    [Fact]
    public void Bind_FlatKeys_MapOntoOptions()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:TenantId"] = "tenant",
            ["Auth:ClientId"] = "client",
            ["Auth:AppRoles:AnnotationRead"] = "role-read",
            ["Auth:AppRoles:AnnotationWrite"] = "role-write",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:Issuer"] = "https://auth.example/",
            ["Auth:AuthKit:Audience"] = "https://storyteller.42for.net",
            ["Auth:AuthKit:AuthKitDomain"] = "https://auth.example",
            ["Auth:AuthKit:DefaultUserScopes:0"] = "User.Impersonation",
            ["Auth:AuthKit:DefaultUserScopes:1"] = "Annotation.Read",
            ["Auth:AuthKit:PermissionMap:annotation-read"] = "Annotation.Read",
        });

        options.Provider.ShouldBe(IdentityProviderKind.AuthKit);
        options.TenantId.ShouldBe("tenant");
        options.ClientId.ShouldBe("client");
        options.AppRoles["AnnotationRead"].ShouldBe("role-read");
        options.AppRoles["AnnotationWrite"].ShouldBe("role-write");
        options.AuthKit.ClientId.ShouldBe("client_123");
        options.AuthKit.Issuer.ShouldBe("https://auth.example/");
        options.AuthKit.Audience.ShouldBe("https://storyteller.42for.net");
        options.AuthKit.AuthKitDomain.ShouldBe("https://auth.example");
        options.AuthKit.DefaultUserScopes.ShouldBe(["User.Impersonation", "Annotation.Read"]);
        options.AuthKit.PermissionMap["annotation-read"].ShouldBe("Annotation.Read");
    }

    [Fact]
    public void Validate_EntraWithTenantAndClient_Succeeds()
    {
        var result = _validator.Validate(null, EntraOptions("tenant", "client"));

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "client")]
    [InlineData("tenant", null)]
    [InlineData(" ", "client")]
    [InlineData("tenant", " ")]
    public void Validate_EntraMissingRequiredValue_Fails(string? tenantId, string? clientId)
    {
        var result = _validator.Validate(null, EntraOptions(tenantId, clientId));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull();
        result.FailureMessage.ShouldContain("Auth:ClientId");
        result.FailureMessage.ShouldContain("Auth:TenantId");
    }

    [Fact]
    public void Validate_AuthKitRequiresOnlyClientId()
    {
        var result = _validator.Validate(null, new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions { ClientId = "client_123" },
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AuthKitMissingClientId_Fails()
    {
        var result = _validator.Validate(null, new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull();
        result.FailureMessage.ShouldContain("Auth:AuthKit:ClientId");
    }

    [Fact]
    public void Validate_UnknownProvider_Fails()
    {
        var result = _validator.Validate(null, new UserAuthenticationOptions
        {
            Provider = (IdentityProviderKind)42,
            ClientId = "client",
            TenantId = "tenant",
        });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Bind_DelegatedClients_MapOntoOptions()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auth:TenantId"] = "tenant",
            ["Auth:ClientId"] = "client",
            ["Auth:DelegatedClients:0:ClientId"] = McpClientId,
            ["Auth:DelegatedClients:0:TenantId"] = McpTenantId,
        });

        options.DelegatedClients.Count.ShouldBe(1);
        options.DelegatedClients[0].ClientId.ShouldBe(McpClientId);
        options.DelegatedClients[0].TenantId.ShouldBe(McpTenantId);
    }

    [Fact]
    public void Bind_NoDelegatedClients_IsEmpty()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auth:TenantId"] = "tenant",
            ["Auth:ClientId"] = "client",
        });

        options.DelegatedClients.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_EntraWithDelegatedClient_Succeeds()
    {
        var options = EntraOptions("tenant", "client");
        options.DelegatedClients.Add(new DelegatedClientOptions { ClientId = McpClientId, TenantId = McpTenantId });

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", McpTenantId)]
    [InlineData("mcp-app", McpTenantId)]
    [InlineData(McpClientId, "")]
    [InlineData(McpClientId, "common")]
    public void Validate_DelegatedClientWithoutGuids_Fails(string clientId, string tenantId)
    {
        var options = EntraOptions("tenant", "client");
        options.DelegatedClients.Add(new DelegatedClientOptions { ClientId = clientId, TenantId = tenantId });

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull();
        result.FailureMessage.ShouldContain("Auth:DelegatedClients");
    }

    [Fact]
    public void Validate_DelegatedClientEqualToTheApiClient_Fails()
    {
        var options = EntraOptions("tenant", McpClientId.ToUpperInvariant());
        options.DelegatedClients.Add(new DelegatedClientOptions { ClientId = McpClientId, TenantId = McpTenantId });

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull();
        result.FailureMessage.ShouldContain("other than Auth:ClientId");
    }

    [Theory]
    [InlineData("{6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11}", McpClientId)]
    [InlineData("6c0f1a2e1f434b0e9a4d2f5e8f0c9b11", McpClientId)]
    [InlineData(McpClientId, "{6C0F1A2E-1F43-4B0E-9A4D-2F5E8F0C9B11}")]
    [InlineData(McpClientId, "6c0f1a2e1f434b0e9a4d2f5e8f0c9b11")]
    public void Validate_DelegatedClientEqualToTheApiClientInAnotherGuidForm_Fails(string apiClientId, string delegatedClientId)
    {
        var options = EntraOptions("tenant", apiClientId);
        options.DelegatedClients.Add(new DelegatedClientOptions { ClientId = delegatedClientId, TenantId = McpTenantId });

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull();
        result.FailureMessage.ShouldContain("other than Auth:ClientId");
    }

    [Theory]
    [InlineData("{6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11}", "{8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d}")]
    [InlineData("6c0f1a2e1f434b0e9a4d2f5e8f0c9b11", "8a7b6c5d4e3f4a1b8c9d0e1f2a3b4c5d")]
    public void Validate_DelegatedClientInAnotherGuidForm_Succeeds(string clientId, string tenantId)
    {
        var options = EntraOptions("tenant", "client");
        options.DelegatedClients.Add(new DelegatedClientOptions { ClientId = clientId, TenantId = tenantId });

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_DelegatedClientsUnderAuthKit_Fail()
    {
        var result = _validator.Validate(null, new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions { ClientId = "client_123" },
            DelegatedClients = [new DelegatedClientOptions { ClientId = McpClientId, TenantId = McpTenantId }],
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull();
        result.FailureMessage.ShouldContain("Entra ID only");
    }

    [Fact]
    public async Task AddEntraIdUserAuthentication_MissingSettings_FailsWhenTheHostStarts()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:TenantId"] = string.Empty,
            ["Auth:ClientId"] = string.Empty,
        });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        exception.Message.ShouldContain("Auth:ClientId");
    }

    [Fact]
    public async Task AddEntraIdUserAuthentication_EntraSettings_RegistersTheValidator()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:TenantId"] = "tenant-id",
            ["Auth:ClientId"] = "client-id",
            ["Auth:AppRoles:AnnotationRead"] = "role-read",
        });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value;
        options.Provider.ShouldBe(IdentityProviderKind.EntraId);
        options.TenantId.ShouldBe("tenant-id");
        options.ClientId.ShouldBe("client-id");
        options.AppRoles["AnnotationRead"].ShouldBe("role-read");
        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeOfType<EntraIdBearerTokenValidator>();
        host.Services.GetRequiredService<IBearerClaimsNormalizer>().ShouldBeOfType<EntraIdClaimNormalizer>();
        host.Services.GetService<IUserProfileResolver>().ShouldBeNull();

        await host.StopAsync();
    }

    private static UserAuthenticationOptions EntraOptions(string? tenantId, string? clientId)
    {
        return new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.EntraId,
            TenantId = tenantId,
            ClientId = clientId,
        };
    }

    private static UserAuthenticationOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = configuration.GetSection(UserAuthenticationOptions.SectionName).Get<UserAuthenticationOptions>();
        options.ShouldNotBeNull();
        return options;
    }

    private static IHost BuildHost(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddEntraIdUserAuthentication(builder.Configuration);
        return builder.Build();
    }
}
