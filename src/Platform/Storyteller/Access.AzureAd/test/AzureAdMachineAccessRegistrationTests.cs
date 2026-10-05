using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class AzureAdMachineAccessRegistrationTests
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Auth:Provider"] = "AuthKit",
        ["Auth:AuthKit:ClientId"] = "client_123",
        ["Auth:TenantId"] = "tenant-id",
        ["Auth:ClientId"] = "api-client",
        ["Auth:AppRoles:DefaultRead"] = "008029ec-407f-48b2-8618-f38b8e03eebf",
        ["Auth:AppRoles:DefaultReadWrite"] = "6eb2cfb3-a936-4077-8a58-620896871d32",
        ["MachineAuth:AzureAd:TenantId"] = "machine-tenant",
    };

    [Fact]
    public async Task AddAzureAdMachineAccess_RegistersTheProviderNextToAnotherUserProvider()
    {
        using var host = BuildHost(Settings);

        await host.StartAsync();

        host.Services.GetRequiredService<IIdentityProviderMachineAccessService>().ShouldBeOfType<AzureAdMachineAccessService>();
        host.Services.GetRequiredService<IMachineTokenValidator>().ShouldBeOfType<EntraIdMachineTokenValidator>();
        host.Services.GetService<IMachineAccessService>().ShouldBeNull();
        host.Services.GetRequiredService<IOptions<AzureAdMachineAccessOptions>>().Value.TenantId.ShouldBe("machine-tenant");

        await host.StopAsync();
    }

    [Fact]
    public async Task AddAzureAdMachineAccess_MissingSettings_FailsWhenTheHostStarts()
    {
        var settings = new Dictionary<string, string?>(Settings)
        {
            ["MachineAuth:AzureAd:TenantId"] = string.Empty,
            ["Auth:AppRoles:DefaultReadWrite"] = "not-a-guid",
        };
        using var host = BuildHost(settings);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("MachineAuth:AzureAd:TenantId");
        exception.Message.ShouldContain("Auth:AppRoles:DefaultReadWrite");
    }

    [Fact]
    public async Task EntraUsersAndEntraMachines_ShareOneTokenValidator()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(Settings) { ["Auth:Provider"] = "EntraId" });
        builder.Services.AddEntraIdUserAuthentication(builder.Configuration);
        builder.Services.AddAzureAdMachineAccess(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetRequiredService<IBearerTokenValidator>()
            .ShouldBeSameAs(host.Services.GetRequiredService<EntraIdBearerTokenValidator>());

        await host.StopAsync();
    }

    [Fact]
    public void AddAzureAdMachineAccess_AfterAnotherMachineProvider_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdentityProviderMachineAccessService>(_ => throw new NotSupportedException());

        var exception = Should.Throw<InvalidOperationException>(
            () => services.AddAzureAdMachineAccess(new ConfigurationBuilder().Build()));

        exception.Message.ShouldContain("Entra ID");
    }

    private static IHost BuildHost(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddAzureAdMachineAccess(builder.Configuration);
        return builder.Build();
    }
}
