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
            ["Auth:AuthKit:PermissionMap:storyteller:configuration:secrets"] = "Configuration.Secrets",
            ["Auth:AuthKit:PermissionMap:annotation-write"] = "Annotation.ReadWrite",
        }));

        options.AuthKit.PermissionMap.ShouldBe(
            new Dictionary<string, string>
            {
                ["storyteller:annotation-read"] = "Annotation.Read",
                ["storyteller:configuration:secrets"] = "Configuration.Secrets",
                ["annotation-write"] = "Annotation.ReadWrite",
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
                    "storyteller:admin": "Default.ReadWrite Configuration.Secrets"
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
        options.AuthKit.PermissionMap["storyteller:admin"].ShouldBe("Default.ReadWrite Configuration.Secrets");
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

    private static IHost BuildHost(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddAuthKitUserAuthentication(builder.Configuration);
        return builder.Build();
    }
}
