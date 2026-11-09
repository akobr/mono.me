using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// MachineAuth:DisabledCredentialKinds is bound from the configuration and checked when the options are first read.
public class MachineAuthenticationOptionsValidatorTests
{
    [Fact]
    public void TheTurnedOffKinds_AreBoundFromTheConfiguration()
    {
        var options = Build(new()
        {
            ["MachineAuth:DisabledCredentialKinds:0"] = "Certificate",
            ["MachineAuth:DisabledCredentialKinds:1"] = "CertificateAndApiKey",
        });

        options.Value.DisabledCredentialKinds.ShouldBe([MachineCredentialKind.Certificate, MachineCredentialKind.CertificateAndApiKey]);
    }

    [Fact]
    public void WithoutTheSetting_NoKindIsTurnedOff()
    {
        Build(new()).Value.DisabledCredentialKinds.ShouldBeEmpty();
    }

    [Fact]
    public void TheDefaultKindTurnedOff_FailsTheOptions()
    {
        var options = Build(new()
        {
            ["MachineAuth:DefaultCredentialKind"] = "Certificate",
            ["MachineAuth:DisabledCredentialKinds:0"] = "Certificate",
        });

        var exception = Should.Throw<OptionsValidationException>(() => options.Value);
        exception.Message.ShouldContain("MachineAuth:DefaultCredentialKind Certificate is turned off");
    }

    private static IOptions<MachineAuthenticationOptions> Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddApiKeyMachineAccess()
            .AddCertificateMachineAccess(configuration);

        return services.BuildServiceProvider().GetRequiredService<IOptions<MachineAuthenticationOptions>>();
    }
}
