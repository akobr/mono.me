using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class UserAuthenticationOptionsTests
{
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
            ["Auth:AppRoles:DefaultRead"] = "role-read",
            ["Auth:AppRoles:DefaultReadWrite"] = "role-write",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:Issuer"] = "https://auth.example/",
            ["Auth:AuthKit:Audience"] = "https://storyteller.42for.net",
            ["Auth:AuthKit:AuthKitDomain"] = "https://auth.example",
            ["Auth:AuthKit:DefaultUserScopes:0"] = "User.Impersonation",
            ["Auth:AuthKit:DefaultUserScopes:1"] = "Default.Read",
            ["Auth:AuthKit:PermissionMap:annotation-read"] = "Annotation.Read",
        });

        options.Provider.ShouldBe(IdentityProviderKind.AuthKit);
        options.TenantId.ShouldBe("tenant");
        options.ClientId.ShouldBe("client");
        options.AppRoles["DefaultRead"].ShouldBe("role-read");
        options.AppRoles["DefaultReadWrite"].ShouldBe("role-write");
        options.AuthKit.ClientId.ShouldBe("client_123");
        options.AuthKit.Issuer.ShouldBe("https://auth.example/");
        options.AuthKit.Audience.ShouldBe("https://storyteller.42for.net");
        options.AuthKit.AuthKitDomain.ShouldBe("https://auth.example");
        options.AuthKit.DefaultUserScopes.ShouldBe(["User.Impersonation", "Default.Read"]);
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
            ["Auth:AppRoles:DefaultRead"] = "role-read",
        });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value;
        options.Provider.ShouldBe(IdentityProviderKind.EntraId);
        options.TenantId.ShouldBe("tenant-id");
        options.ClientId.ShouldBe("client-id");
        options.AppRoles["DefaultRead"].ShouldBe("role-read");
        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeOfType<EntraIdBearerTokenValidator>();

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
