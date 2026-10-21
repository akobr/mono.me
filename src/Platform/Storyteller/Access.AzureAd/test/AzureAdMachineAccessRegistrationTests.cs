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
        ["Auth:AppRoles:AnnotationRead"] = "008029ec-407f-48b2-8618-f38b8e03eebf",
        ["Auth:AppRoles:AnnotationWrite"] = "6eb2cfb3-a936-4077-8a58-620896871d32",
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
    public async Task AddAzureAdMachineAccess_WithoutAnyAppRole_Starts()
    {
        // Creation checks the roles of the requested scopes; the start needs none.
        var settings = Settings
            .Where(setting => !setting.Key.StartsWith("Auth:AppRoles:", StringComparison.Ordinal))
            .ToDictionary(setting => setting.Key, setting => setting.Value);
        using var host = BuildHost(settings);

        await host.StartAsync();

        host.Services.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value.AppRoles.ShouldBeEmpty();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddAzureAdMachineAccess_MissingSettings_FailsWhenTheHostStarts()
    {
        var settings = new Dictionary<string, string?>(Settings)
        {
            ["MachineAuth:AzureAd:TenantId"] = string.Empty,
            ["Auth:AppRoles:AnnotationWrite"] = "not-a-guid",
        };
        using var host = BuildHost(settings);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("MachineAuth:AzureAd:TenantId");
        exception.Message.ShouldContain("Auth:AppRoles:AnnotationWrite");
    }

    [Fact]
    public async Task AddAzureAdMachineAccess_ScopeRoles_AreOptionalButMustBeIdsWhenSet()
    {
        var settings = new Dictionary<string, string?>(Settings)
        {
            ["Auth:AppRoles:ConfigurationTransfer"] = "1b2c3d4e-5f60-4718-892a-0b1c2d3e4f50",
            ["Auth:AppRoles:SchemaWrite"] = "not-a-guid",
            ["Auth:AppRoles:SecretsWrite"] = "secrets-write",
            ["Auth:AppRoles:TemplateWrite"] = string.Empty,
        };
        using var host = BuildHost(settings);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.ShouldBe(
            [
                "Auth:AppRoles:SecretsWrite must be the ID of the API app role Secrets.Write when it is set.",
                "Auth:AppRoles:SchemaWrite must be the ID of the API app role Schema.Write when it is set.",
            ]);
    }

    [Fact]
    public async Task AddAzureAdMachineAccess_TwoScopesWithOneRole_FailsWhenTheHostStarts()
    {
        // The same ID in another spelling is still the same role.
        var settings = new Dictionary<string, string?>(Settings)
        {
            ["Auth:AppRoles:AnnotationWrite"] = "008029EC-407F-48B2-8618-F38B8E03EEBF",
            ["Auth:AppRoles:ConfigurationRead"] = "1b2c3d4e-5f60-4718-892a-0b1c2d3e4f50",
            ["Auth:AppRoles:KeyValueManage"] = "1b2c3d4e-5f60-4718-892a-0b1c2d3e4f50",
        };
        using var host = BuildHost(settings);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.ShouldBe(
            [
                "Auth:AppRoles:AnnotationRead and Auth:AppRoles:AnnotationWrite share the role ID 008029ec-407f-48b2-8618-f38b8e03eebf; every scope needs an app role of its own.",
                "Auth:AppRoles:ConfigurationRead and Auth:AppRoles:KeyValueManage share the role ID 1b2c3d4e-5f60-4718-892a-0b1c2d3e4f50; every scope needs an app role of its own.",
            ]);
    }

    [Theory]
    [InlineData("DefaultRead", "Auth:AppRoles:AnnotationRead, Auth:AppRoles:ConfigurationRead")]
    [InlineData("DefaultReadWrite", "Auth:AppRoles:AnnotationRead, Auth:AppRoles:AnnotationWrite, Auth:AppRoles:ConfigurationRead, Auth:AppRoles:ConfigurationWrite")]
    [InlineData("SecretsReadWrite", "Auth:AppRoles:SecretsRead, Auth:AppRoles:SecretsWrite")]
    public async Task AddAzureAdMachineAccess_RemovedPresetRole_FailsWhenTheHostStarts(string key, string replacements)
    {
        var settings = new Dictionary<string, string?>(Settings)
        {
            [$"Auth:AppRoles:{key}"] = "9a8b7c6d-5e4f-4321-8fed-cba987654321",
        };
        using var host = BuildHost(settings);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.ShouldBe(
            [$"Auth:AppRoles:{key} was removed: Entra ID machines get one app role per scope. Set {replacements} instead."]);
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
