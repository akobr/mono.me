using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public class FederationRegistrationTests
{
    [Fact]
    public async Task AddFederatedMachineAccess_RegistersTheExchangeTheMachineServiceAndAnExtraTokenValidator()
    {
        var otherValidator = Mock.Of<IMachineTokenValidator>();
        using var host = BuildHost([], services => services.AddSingleton(otherValidator));

        await host.StartAsync();

        host.Services.GetRequiredService<IFederatedTokenExchange>().ShouldBeOfType<FederatedTokenExchangeService>();
        host.Services.GetRequiredService<IFederatedMachineAccessService>().ShouldBeOfType<FederatedMachineAccessService>();
        host.Services.GetRequiredService<IFederatedIssuerStore>().ShouldBeOfType<InMemoryFederatedIssuerStore>();
        host.Services.GetRequiredService<ITokenSigningKeyStore>().ShouldBeOfType<InMemoryTokenSigningKeyStore>();

        var validators = host.Services.GetServices<IMachineTokenValidator>().ToList();
        validators.Count.ShouldBe(2);
        validators.ShouldContain(otherValidator);
        validators.OfType<StorytellerTokenValidator>().Count().ShouldBe(1);

        await host.StopAsync();
    }

    [Fact]
    public async Task AddFederatedMachineAccess_KeepsStoresRegisteredBefore()
    {
        var issuers = Mock.Of<IFederatedIssuerStore>();
        var signingKeys = Mock.Of<ITokenSigningKeyStore>();
        using var host = BuildHost([], services => services.AddSingleton(issuers).AddSingleton(signingKeys));

        await host.StartAsync();

        host.Services.GetRequiredService<IFederatedIssuerStore>().ShouldBeSameAs(issuers);
        host.Services.GetRequiredService<ITokenSigningKeyStore>().ShouldBeSameAs(signingKeys);
        await host.StopAsync();
    }

    [Fact]
    public async Task AddFederatedMachineAccess_WorksNextToAClientCredentialsProvider_AndOnlyOnce()
    {
        using var host = BuildHost([], services =>
        {
            services.AddSingleton(Mock.Of<IIdentityProviderMachineAccessService>());
            services.AddFederatedMachineAccess(new ConfigurationBuilder().Build());
        });

        await host.StartAsync();

        host.Services.GetServices<IMachineTokenValidator>().OfType<StorytellerTokenValidator>().Count().ShouldBe(1);
        host.Services.GetServices<IFederatedMachineAccessService>().Count().ShouldBe(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task Options_DeriveIssuerAndAudienceFromTheTrustDomain()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["MachineAuth:TrustDomain"] = "acme.example" });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<FederationOptions>>().Value;
        options.Issuer.ShouldBe("urn:storyteller:acme.example");
        options.Audience.ShouldBe("urn:storyteller:acme.example:api");
        host.Services.GetServices<IMachineTokenValidator>().Single().OwnsIssuer("urn:storyteller:acme.example").ShouldBeTrue();
        await host.StopAsync();
    }

    [Fact]
    public async Task InvalidOptions_FailWhenTheHostStarts()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["MachineAuth:Federation:TokenLifetimeMinutes"] = "120" });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("TokenLifetimeMinutes");
    }

    private static IHost BuildHost(Dictionary<string, string?> values, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.Configure<MachineAuthenticationOptions>(builder.Configuration.GetSection(MachineAuthenticationOptions.SectionName));
        builder.Services.AddSingleton(Mock.Of<IAccessService>());
        configure?.Invoke(builder.Services);
        builder.Services.AddFederatedMachineAccess(builder.Configuration);
        return builder.Build();
    }
}
