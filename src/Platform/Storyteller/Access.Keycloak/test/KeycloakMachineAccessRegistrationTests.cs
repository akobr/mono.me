using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

public class KeycloakMachineAccessRegistrationTests
{
    [Fact]
    public async Task AddKeycloakMachineAccess_RegistersTheProviderAndItsTokenValidator()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Keycloak:ServerUrl"] = "https://keycloak.example",
            ["Keycloak:Realm"] = "storyteller",
            ["Keycloak:AdminClientSecret"] = "admin-secret",
        });

        await host.StartAsync();

        host.Services.GetRequiredService<IIdentityProviderMachineAccessService>().ShouldBeOfType<KeycloakMachineAccessService>();
        host.Services.GetRequiredService<IMachineTokenValidator>().ShouldBeOfType<KeycloakMachineTokenValidator>();
        host.Services.GetService<IMachineAccessService>().ShouldBeNull();
        var options = host.Services.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        options.Audience.ShouldBe("storyteller");
        options.GetTokenEndpoint().ShouldBe("https://keycloak.example/realms/storyteller/protocol/openid-connect/token");

        await host.StopAsync();
    }

    [Fact]
    public async Task AddKeycloakMachineAccess_WithoutAdminCredentials_FailsWhenTheHostStarts()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Keycloak:ServerUrl"] = "not a url",
            ["Keycloak:AdminUsername"] = "admin",
        });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("Keycloak:ServerUrl");
        exception.Message.ShouldContain("Keycloak:AdminClientSecret");
    }

    [Theory]
    [InlineData("http://keycloak.example", false)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("https://127.0.0.1:8443", true)]
    public void ServerUrl_RequiresHttpsExceptOnLoopback(string serverUrl, bool allowed)
    {
        var result = new KeycloakOptionsValidator().Validate(null, new KeycloakOptions
        {
            ServerUrl = serverUrl,
            Realm = "storyteller",
            Audience = "storyteller-api",
            AdminClientSecret = "admin-secret",
        });

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

    [Fact]
    public void AddKeycloakMachineAccess_AfterAnotherMachineProvider_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdentityProviderMachineAccessService>(_ => throw new NotSupportedException());

        var exception = Should.Throw<InvalidOperationException>(
            () => services.AddKeycloakMachineAccess(new ConfigurationBuilder().Build()));

        exception.Message.ShouldContain("Keycloak");
    }

    private static IHost BuildHost(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddKeycloakMachineAccess(builder.Configuration);
        return builder.Build();
    }
}
