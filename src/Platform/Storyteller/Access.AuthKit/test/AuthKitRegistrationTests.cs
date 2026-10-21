using System.Text;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitRegistrationTests
{
    private readonly UserAuthenticationOptionsValidator _validator = new();

    [Fact]
    public void AddUserAuthenticationOptions_FlatKeys_BindPermissionSlugsWithColons()
    {
        var options = Resolve(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:PermissionMap:storyteller:annotation-read"] = "Annotation.Read",
            ["Auth:AuthKit:PermissionMap:storyteller:secrets:read"] = "Secrets.Read",
            ["Auth:AuthKit:PermissionMap:annotation-write"] = "Annotation.Write",
        }));

        options.AuthKit.PermissionMap.ShouldBe(
            new Dictionary<string, string>
            {
                ["storyteller:annotation-read"] = "Annotation.Read",
                ["storyteller:secrets:read"] = "Secrets.Read",
                ["annotation-write"] = "Annotation.Write",
            },
            ignoreOrder: true);
    }

    [Fact]
    public void AddUserAuthenticationOptions_JsonObject_BindsPermissionSlugsWithColons()
    {
        const string json = """
            {
              "Auth": {
                "Provider": "AuthKit",
                "AuthKit": {
                  "ClientId": "client_123",
                  "DefaultUserScopes": [ "User.Impersonation" ],
                  "PermissionMap": {
                    "storyteller:annotation-read": "Annotation.Read",
                    "storyteller:impersonate": "User.Impersonation"
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var options = Resolve(new ConfigurationBuilder().AddJsonStream(stream));

        options.AuthKit.DefaultUserScopes.ShouldBe(["User.Impersonation"]);
        options.AuthKit.PermissionMap.Count.ShouldBe(2);
        options.AuthKit.PermissionMap["storyteller:annotation-read"].ShouldBe("Annotation.Read");
        options.AuthKit.PermissionMap["storyteller:impersonate"].ShouldBe("User.Impersonation");
    }

    [Fact]
    public void GetJwksUri_DefaultsToTheClientJwksOnTheApiBaseUrl()
    {
        new AuthKitOptions { ClientId = "client_123" }.GetJwksUri()
            .ShouldBe("https://api.workos.com/sso/jwks/client_123");
        new AuthKitOptions { ClientId = "client_123", ApiBaseUrl = "https://workos.example/" }.GetJwksUri()
            .ShouldBe("https://workos.example/sso/jwks/client_123");
        new AuthKitOptions { ClientId = "client_123", JwksUri = "https://auth.example.com/oauth2/jwks" }.GetJwksUri()
            .ShouldBe("https://auth.example.com/oauth2/jwks");
    }

    [Fact]
    public void Validate_AuthKitWithDefaults_Succeeds()
    {
        var result = _validator.Validate(null, AuthKitOptions(_ => { }));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AuthKitBlankIssuer_Fails()
    {
        var result = _validator.Validate(null, AuthKitOptions(authKit => authKit.Issuer = " "));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:AuthKit:Issuer");
    }

    [Theory]
    [InlineData("http://api.workos.com/sso/jwks/client_123", null)]
    [InlineData("not a url", null)]
    [InlineData(null, "http://api.workos.com")]
    public void Validate_AuthKitJwksMustBeHttps(string? jwksUri, string? apiBaseUrl)
    {
        var result = _validator.Validate(null, AuthKitOptions(authKit =>
        {
            authKit.JwksUri = jwksUri;
            authKit.ApiBaseUrl = apiBaseUrl ?? authKit.ApiBaseUrl;
        }));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:AuthKit:JwksUri");
    }

    [Fact]
    public async Task AddAuthKitUserAuthentication_RegistersTheAuthKitValidatorAndNormalizer()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:Audience"] = "https://storyteller.42for.net",
        });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value;
        options.Provider.ShouldBe(IdentityProviderKind.AuthKit);
        options.AuthKit.Audience.ShouldBe("https://storyteller.42for.net");
        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeOfType<AuthKitBearerTokenValidator>();
        host.Services.GetRequiredService<IBearerClaimsNormalizer>()
            .ShouldBeSameAs(host.Services.GetRequiredService<AuthKitClaimNormalizer>());
        host.Services.GetRequiredService<IUserProfileResolver>().ShouldBeOfType<AuthKitUserProfileResolver>();
        host.Services.GetRequiredService<WorkOsManagementClient>().IsConfigured.ShouldBeFalse();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddAuthKitUserAuthentication_MissingClientId_FailsWhenTheHostStarts()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = string.Empty,
        });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        exception.Message.ShouldContain("Auth:AuthKit:ClientId");
    }

    [Theory]
    [InlineData("https://example.authkit.app", "org_machines", true)]
    [InlineData("https://example.authkit.app", null, false)]
    [InlineData("", "org_machines", false)]
    public void HasMachineAccess_NeedsTheDomainAndTheMachineOrganization(string domain, string? organization, bool expected)
    {
        var options = new AuthKitOptions { AuthKitDomain = domain, MachineOrganizationId = organization };

        options.HasMachineAccess().ShouldBe(expected);
        new AuthKitOptions { AuthKitDomain = "https://example.authkit.app/" }.GetMachineJwksUri()
            .ShouldBe("https://example.authkit.app/oauth2/jwks");
    }

    [Fact]
    public async Task AddAuthKitMachineAccess_RegistersTheIdentityProviderMachineService()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "AuthKit",
                ["Auth:AuthKit:ClientId"] = "client_123",
                ["Auth:AuthKit:ApiKey"] = "sk_test",
                ["Auth:AuthKit:AuthKitDomain"] = "https://example.authkit.app",
                ["Auth:AuthKit:MachineOrganizationId"] = "org_machines",
            },
            withMachineAccess: true);

        await host.StartAsync();

        host.Services.GetRequiredService<IIdentityProviderMachineAccessService>().ShouldBeOfType<AuthKitMachineAccessService>();
        host.Services.GetRequiredService<IMachineTokenValidator>().ShouldBeOfType<AuthKitMachineTokenValidator>();
        host.Services.GetRequiredService<WorkOsManagementClient>().IsConfigured.ShouldBeTrue();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddAuthKitMachineAccess_WorksWithoutAuthKitUsers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:TenantId"] = "tenant-id",
            ["Auth:ClientId"] = "api-client",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:ApiKey"] = "sk_test",
            ["Auth:AuthKit:AuthKitDomain"] = "https://example.authkit.app",
            ["Auth:AuthKit:MachineOrganizationId"] = "org_machines",
        });
        builder.Services.AddAuthKitMachineAccess(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetRequiredService<IIdentityProviderMachineAccessService>().ShouldBeOfType<AuthKitMachineAccessService>();
        host.Services.GetRequiredService<IMachineTokenValidator>().ShouldBeOfType<AuthKitMachineTokenValidator>();
        host.Services.GetService<IBearerTokenValidator>().ShouldBeNull();

        await host.StopAsync();
    }

    [Fact]
    public void UserAndMachineRegistration_BindTheOptionsOnce()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:ApiKey"] = "sk_test",
            ["Auth:AuthKit:AuthKitDomain"] = "https://example.authkit.app",
            ["Auth:AuthKit:MachineOrganizationId"] = "org_machines",
            ["Auth:AuthKit:DefaultUserScopes:0"] = "User.Impersonation",
            ["Auth:AuthKit:DefaultUserScopes:1"] = "Annotation.Read",
        }).Build();
        var services = new ServiceCollection();

        services.AddAuthKitUserAuthentication(configuration);
        services.AddAuthKitMachineAccess(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value.AuthKit.DefaultUserScopes
            .ShouldBe(["User.Impersonation", "Annotation.Read"]);
    }

    [Fact]
    public void AddAuthKitMachineAccess_AfterAnotherMachineProvider_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdentityProviderMachineAccessService>(_ => throw new NotSupportedException());

        var exception = Should.Throw<InvalidOperationException>(
            () => services.AddAuthKitMachineAccess(new ConfigurationBuilder().Build()));

        exception.Message.ShouldContain("AuthKit");
    }

    [Fact]
    public async Task AddAuthKitMachineAccess_MissingKeyOrDomain_FailsWhenTheHostStarts()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "AuthKit",
                ["Auth:AuthKit:ClientId"] = "client_123",
                ["Auth:AuthKit:ApiKey"] = string.Empty,
                ["Auth:AuthKit:AuthKitDomain"] = "http://example.authkit.app",
                ["Auth:AuthKit:MachineOrganizationId"] = "org_machines",
            },
            withMachineAccess: true);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        exception.Message.ShouldContain("Auth:AuthKit:ApiKey");
        exception.Message.ShouldContain("Auth:AuthKit:AuthKitDomain");
    }

    [Fact]
    public async Task AddAuthKitMachineAccess_OnePermissionPerScope_Starts()
    {
        // User.Impersonation serves users only, so two permissions may give it; values are trimmed.
        using var host = BuildHost(
            MachineSettings(new()
            {
                ["storyteller:annotation-read"] = "Annotation.Read",
                ["storyteller:annotation-write"] = "Annotation.Write",
                ["storyteller:configuration-read"] = "Configuration.Read",
                ["storyteller:configuration-write"] = "Configuration.Write",
                ["storyteller:secrets-read"] = "Secrets.Read",
                ["storyteller:secrets-write"] = "Secrets.Write",
                ["storyteller:configuration-transfer"] = "Configuration.Transfer",
                ["storyteller:schema-write"] = "Schema.Write",
                ["storyteller:template-write"] = "Template.Write",
                ["storyteller:annotation-manage"] = "Annotation.Manage",
                ["storyteller:keyvalue-manage"] = " KeyValue.Manage ",
                ["storyteller:impersonate"] = "User.Impersonation",
                ["storyteller:sign-in"] = "User.Impersonation",
            }),
            withMachineAccess: true);

        await host.StartAsync();

        host.Services.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value.AuthKit.PermissionMap.Count.ShouldBe(13);

        await host.StopAsync();
    }

    [Theory]
    [InlineData("Annotation.Read Configuration.Read")]
    [InlineData("Annotation.Read,Configuration.Read")]
    [InlineData("Default.ReadWrite")]
    [InlineData("Annotation.ReadWrite")]
    [InlineData("Configuration.Secrets")]
    [InlineData("annotation.read")]
    [InlineData("AnnotationRead")]
    public async Task AddAuthKitMachineAccess_PermissionNotExactlyOneScope_FailsWhenTheHostStarts(string value)
    {
        using var host = BuildHost(
            MachineSettings(new() { ["storyteller:annotation-read"] = "Annotation.Read", ["storyteller:admin"] = value }),
            withMachineAccess: true);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        exception.Message.ShouldContain(
            $"Auth:AuthKit:PermissionMap maps storyteller:admin to '{value}', which is not exactly one of Annotation.Read, Annotation.Write, "
            + "Configuration.Read, Configuration.Write, Secrets.Read, Secrets.Write, Configuration.Transfer, Schema.Write, Template.Write, "
            + "Annotation.Manage, KeyValue.Manage or User.Impersonation.");
        exception.Message.ShouldNotContain("storyteller:annotation-read");
    }

    [Fact]
    public async Task AddAuthKitMachineAccess_TwoPermissionsForOneMachineScope_FailsWhenTheHostStarts()
    {
        using var host = BuildHost(
            MachineSettings(new() { ["storyteller:reader"] = "Annotation.Read", ["storyteller:annotation-read"] = "Annotation.Read" }),
            withMachineAccess: true);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        exception.Message.ShouldContain(
            "Auth:AuthKit:PermissionMap maps both storyteller:annotation-read and storyteller:reader to Annotation.Read; a machine scope needs exactly one WorkOS permission.");
    }

    private static UserAuthenticationOptions AuthKitOptions(Action<AuthKitOptions> configure)
    {
        var authKit = new AuthKitOptions { ClientId = "client_123" };
        configure(authKit);
        return new UserAuthenticationOptions { Provider = IdentityProviderKind.AuthKit, AuthKit = authKit };
    }

    private static UserAuthenticationOptions Resolve(IConfigurationBuilder builder)
    {
        var configuration = builder.Build();
        var services = new ServiceCollection();
        services.AddUserAuthenticationOptions(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value;
    }

    private static IHost BuildHost(Dictionary<string, string?> values, bool withMachineAccess = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddAuthKitUserAuthentication(builder.Configuration);

        if (withMachineAccess)
        {
            builder.Services.AddAuthKitMachineAccess(builder.Configuration);
        }

        return builder.Build();
    }

    // Machine settings that pass the startup validation, plus the given PermissionMap.
    private static Dictionary<string, string?> MachineSettings(Dictionary<string, string> permissionMap)
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = "client_123",
            ["Auth:AuthKit:ApiKey"] = "sk_test",
            ["Auth:AuthKit:AuthKitDomain"] = "https://example.authkit.app",
            ["Auth:AuthKit:MachineOrganizationId"] = "org_machines",
        };

        foreach (var (slug, scope) in permissionMap)
        {
            values[$"Auth:AuthKit:PermissionMap:{slug}"] = scope;
        }

        return values;
    }
}
