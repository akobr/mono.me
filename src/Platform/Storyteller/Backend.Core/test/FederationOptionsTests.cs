using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class FederationOptionsTests
{
    [Fact]
    public void AddFederationOptions_DerivesIssuerAndAudienceFromTheTrustDomain()
    {
        var options = Resolve(new Dictionary<string, string?> { ["MachineAuth:TrustDomain"] = "acme.example" });

        options.Issuer.ShouldBe("urn:storyteller:acme.example");
        options.Audience.ShouldBe("urn:storyteller:acme.example:api");
        options.TokenLifetimeMinutes.ShouldBe(15);
        options.ClockSkewSeconds.ShouldBe(30);
        options.MaxSubjectTokenBytes.ShouldBe(16384);
        options.MetadataRefreshMinutes.ShouldBe(60);
        options.SigningKeyAutoBootstrap.ShouldBeTrue();
        options.AllowPrivateNetworks.ShouldBeFalse();
        options.PublicBaseUrl.ShouldBeNull();
    }

    [Fact]
    public void AddFederationOptions_WithoutMachineAuth_UsesTheDefaultTrustDomain()
    {
        var options = Resolve([], bindMachineAuth: false);

        options.Issuer.ShouldBe("urn:storyteller:2s.platform");
        options.Audience.ShouldBe("urn:storyteller:2s.platform:api");
    }

    [Fact]
    public void AddFederationOptions_KeepsConfiguredValues()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["MachineAuth:Federation:Issuer"] = "https://storyteller.example.com",
            ["MachineAuth:Federation:Audience"] = "https://storyteller.example.com/api",
            ["MachineAuth:Federation:PublicBaseUrl"] = "https://storyteller.example.com/api",
            ["MachineAuth:Federation:TokenLifetimeMinutes"] = "60",
            ["MachineAuth:Federation:AllowPrivateNetworks"] = "true",
            ["MachineAuth:Federation:SigningKeyAutoBootstrap"] = "false",
        });

        options.Issuer.ShouldBe("https://storyteller.example.com");
        options.Audience.ShouldBe("https://storyteller.example.com/api");
        options.PublicBaseUrl.ShouldBe("https://storyteller.example.com/api");
        options.TokenLifetimeMinutes.ShouldBe(60);
        options.AllowPrivateNetworks.ShouldBeTrue();
        options.SigningKeyAutoBootstrap.ShouldBeFalse();
    }

    [Theory]
    [InlineData("TokenLifetimeMinutes", "4")]
    [InlineData("TokenLifetimeMinutes", "61")]
    [InlineData("ClockSkewSeconds", "-1")]
    [InlineData("ClockSkewSeconds", "121")]
    [InlineData("MaxSubjectTokenBytes", "1023")]
    [InlineData("MaxSubjectTokenBytes", "32769")]
    [InlineData("MetadataRefreshMinutes", "0")]
    [InlineData("MetadataRefreshMinutes", "1441")]
    [InlineData("PublicBaseUrl", "storyteller.example.com/api")]
    [InlineData("PublicBaseUrl", "ftp://storyteller.example.com/api")]
    [InlineData("PublicBaseUrl", "https://storyteller.example.com/api?x=1")]
    public void AddFederationOptions_RefusesAValueOutOfRange(string setting, string value)
    {
        var exception = Should.Throw<OptionsValidationException>(
            () => Resolve(new Dictionary<string, string?> { [$"MachineAuth:Federation:{setting}"] = value }));

        exception.Message.ShouldContain($"MachineAuth:Federation:{setting}");
    }

    [Theory]
    [InlineData("TokenLifetimeMinutes", "5")]
    [InlineData("TokenLifetimeMinutes", "60")]
    [InlineData("ClockSkewSeconds", "0")]
    [InlineData("ClockSkewSeconds", "120")]
    [InlineData("MaxSubjectTokenBytes", "32768")]
    [InlineData("MetadataRefreshMinutes", "1440")]
    [InlineData("PublicBaseUrl", "http://localhost:7071/api")]
    public void AddFederationOptions_AcceptsTheBounds(string setting, string value)
    {
        Should.NotThrow(() => Resolve(new Dictionary<string, string?> { [$"MachineAuth:Federation:{setting}"] = value }));
    }

    [Fact]
    public void Validator_RequiresIssuerAndAudience()
    {
        var result = new FederationOptionsValidator().Validate(null, new FederationOptions());

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldBe(["MachineAuth:Federation:Issuer is required.", "MachineAuth:Federation:Audience is required."]);
    }

    private static FederationOptions Resolve(Dictionary<string, string?> settings, bool bindMachineAuth = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        if (bindMachineAuth)
        {
            services.Configure<MachineAuthenticationOptions>(configuration.GetSection(MachineAuthenticationOptions.SectionName));
        }

        services.AddFederationOptions(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<FederationOptions>>().Value;
    }
}
