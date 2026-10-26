using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

// Auth:Keycloak, the Keycloak user settings: binding, validation at start and the services AddKeycloakUserAuthentication adds.
public class KeycloakUserAuthenticationTests
{
    private const string Issuer = "https://keycloak.example/realms/storyteller";

    private readonly UserAuthenticationOptionsValidator _validator = new();

    public static TheoryData<string[]> NoClients => new()
    {
        { [] },
        { [" "] },
        { [string.Empty, " "] },
    };

    [Fact]
    public async Task AddKeycloakUserAuthentication_RegistersTheValidatorAndTheNormalizer()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "Keycloak",
            ["Auth:Keycloak:Issuer"] = Issuer,
            ["Auth:Keycloak:Audience"] = "storyteller-api",
            ["Auth:Keycloak:AllowedClientIds:0"] = "sform",
        });

        await host.StartAsync();

        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeOfType<KeycloakBearerTokenValidator>();
        var normalizer = host.Services.GetRequiredService<IBearerClaimsNormalizer>().ShouldBeOfType<KeycloakClaimNormalizer>();
        normalizer.ShouldBeSameAs(host.Services.GetRequiredService<KeycloakClaimNormalizer>());
        host.Services.GetService<IUserProfileResolver>().ShouldBeNull();
        host.Services.GetService<IMachineTokenValidator>().ShouldBeNull();
        host.Services.GetService<IIdentityProviderMachineAccessService>().ShouldBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddKeycloakUserAuthentication_MissingSettings_FailsWhenTheHostStartsWithEveryReason()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "Keycloak",
            ["Auth:Keycloak:Issuer"] = "http://keycloak.example/realms/storyteller",
        });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("Auth:Keycloak:Issuer must use HTTPS");
        exception.Message.ShouldContain("Auth:Keycloak:Audience");
        exception.Message.ShouldContain("Auth:Keycloak:AllowedClientIds");
    }

    // Users and machines of Keycloak have separate settings, so a deployment can add both, on one realm or two.
    [Fact]
    public async Task AddKeycloakUserAuthentication_WithKeycloakMachineAccess_KeepsBothValidators()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "Keycloak",
                ["Auth:Keycloak:Issuer"] = Issuer,
                ["Auth:Keycloak:Audience"] = "storyteller-api",
                ["Auth:Keycloak:AllowedClientIds:0"] = "sform",
                ["Keycloak:ServerUrl"] = "https://keycloak.example",
                ["Keycloak:Realm"] = "storyteller",
                ["Keycloak:AdminClientSecret"] = "admin-secret",
            },
            withMachineAccess: true);

        await host.StartAsync();

        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeOfType<KeycloakBearerTokenValidator>();
        host.Services.GetRequiredService<IMachineTokenValidator>().ShouldBeOfType<KeycloakMachineTokenValidator>();

        await host.StopAsync();
    }

    [Fact]
    public void Bind_KeycloakSettings_MapOntoOptions_WithRoleNamesThatContainColons()
    {
        var services = new ServiceCollection();
        services.AddUserAuthenticationOptions(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "Keycloak",
            ["Auth:Keycloak:Issuer"] = $"{Issuer}/",
            ["Auth:Keycloak:Audience"] = "storyteller-api",
            ["Auth:Keycloak:ClientId"] = "sform",
            ["Auth:Keycloak:AllowedClientIds:0"] = "sform",
            ["Auth:Keycloak:AllowedClientIds:1"] = "admin-ui",
            ["Auth:Keycloak:DefaultUserScopes:0"] = "User.Impersonation",
            ["Auth:Keycloak:RoleMap:storyteller-admin"] = "Annotation.Write",
            ["Auth:Keycloak:RoleMap:storyteller-api:annotation-reader"] = "Annotation.Read",
        }).Build());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value;

        options.Provider.ShouldBe(IdentityProviderKind.Keycloak);
        options.Keycloak.Issuer.ShouldBe($"{Issuer}/");
        options.Keycloak.GetIssuer().ShouldBe(Issuer);
        options.Keycloak.Audience.ShouldBe("storyteller-api");
        options.Keycloak.ClientId.ShouldBe("sform");
        options.Keycloak.AllowedClientIds.ShouldBe(["sform", "admin-ui"]);
        options.Keycloak.DefaultUserScopes.ShouldBe(["User.Impersonation"]);
        options.Keycloak.RoleMap.Count.ShouldBe(2);
        options.Keycloak.RoleMap["storyteller-admin"].ShouldBe("Annotation.Write");
        options.Keycloak.RoleMap["storyteller-api:annotation-reader"].ShouldBe("Annotation.Read");
    }

    // The Configure step that reads Auth:Keycloak:RoleMap runs for every provider, also when Auth:Keycloak is missing.
    [Fact]
    public void Bind_NoKeycloakSettings_AreEmpty()
    {
        var services = new ServiceCollection();
        services.AddUserAuthenticationOptions(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:TenantId"] = "tenant",
            ["Auth:ClientId"] = "client",
        }).Build());
        using var provider = services.BuildServiceProvider();

        var keycloak = provider.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value.Keycloak;

        keycloak.Issuer.ShouldBeEmpty();
        keycloak.Audience.ShouldBeEmpty();
        keycloak.ClientId.ShouldBeNull();
        keycloak.AllowedClientIds.ShouldBeEmpty();
        keycloak.DefaultUserScopes.ShouldBeEmpty();
        keycloak.RoleMap.ShouldNotBeNull().ShouldBeEmpty();
    }

    // The Entra ID rules (Auth:ClientId, Auth:TenantId) do not apply to Keycloak.
    [Fact]
    public void Validate_KeycloakWithIssuerAndAudience_Succeeds()
    {
        _validator.Validate(null, KeycloakOptions(Issuer)).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://keycloak.example/realms/storyteller", true)]
    [InlineData("http://localhost:8080/realms/storyteller", true)]
    [InlineData("http://127.0.0.1:8080/realms/storyteller/", true)]
    [InlineData("http://keycloak.example/realms/storyteller", false)]
    public void Validate_Issuer_RequiresHttpsExceptOnLoopback(string issuer, bool allowed)
    {
        var result = _validator.Validate(null, KeycloakOptions(issuer));

        if (allowed)
        {
            result.Succeeded.ShouldBeTrue();
        }
        else
        {
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldContain("HTTPS");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("keycloak.example/realms/storyteller")]
    [InlineData("ftp://keycloak.example/realms/storyteller")]
    [InlineData("file:///realms/storyteller")]
    public void Validate_IssuerThatIsNotAnHttpUrl_Fails(string issuer)
    {
        var result = _validator.Validate(null, KeycloakOptions(issuer));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:Keycloak:Issuer");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_MissingAudience_Fails(string audience)
    {
        var options = KeycloakOptions(Issuer);
        options.Keycloak.Audience = audience;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:Keycloak:Audience");
    }

    [Theory]
    [MemberData(nameof(NoClients))]
    public void Validate_NoAllowedClientIds_Fails(string[] allowedClientIds)
    {
        var options = KeycloakOptions(Issuer);
        options.Keycloak.AllowedClientIds = allowedClientIds;

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:Keycloak:AllowedClientIds must list at least one client");
    }

    [Fact]
    public void Validate_BlankAllowedClientId_Fails()
    {
        var options = KeycloakOptions(Issuer);
        options.Keycloak.AllowedClientIds = ["sform", " "];

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:Keycloak:AllowedClientIds entries must not be blank");
    }

    // The sign-in client that v1/auth/configuration advertises would otherwise get tokens the API refuses.
    [Theory]
    [InlineData("sform", true)]
    [InlineData(" sform ", true)]
    [InlineData("admin-ui", false)]
    [InlineData("SFORM", false)]
    public void Validate_ClientId_IsOneOfTheAllowedClients(string clientId, bool allowed)
    {
        var options = KeycloakOptions(Issuer);
        options.Keycloak.ClientId = clientId;

        var result = _validator.Validate(null, options);

        if (allowed)
        {
            result.Succeeded.ShouldBeTrue();
        }
        else
        {
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldContain("Auth:Keycloak:ClientId must be one of Auth:Keycloak:AllowedClientIds");
        }
    }

    [Fact]
    public void Validate_EveryKeycloakFailure_IsReported()
    {
        var options = new UserAuthenticationOptions { Provider = IdentityProviderKind.Keycloak };
        options.Keycloak.AllowedClientIds = [string.Empty];

        var result = _validator.Validate(null, options);

        result.Failures.ShouldNotBeNull().Count().ShouldBe(3);
    }

    private static UserAuthenticationOptions KeycloakOptions(string issuer)
    {
        return new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions { Issuer = issuer, Audience = "storyteller-api", AllowedClientIds = ["sform"] },
        };
    }

    private static IHost BuildHost(Dictionary<string, string?> values, bool withMachineAccess = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddKeycloakUserAuthentication(builder.Configuration);

        if (withMachineAccess)
        {
            builder.Services.AddKeycloakMachineAccess(builder.Configuration);
        }

        return builder.Build();
    }
}
