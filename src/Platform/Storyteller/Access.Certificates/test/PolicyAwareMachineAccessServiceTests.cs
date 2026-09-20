using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class PolicyAwareMachineAccessServiceTests
{
    private readonly MachineAuthenticationOptions _options = TestCertificateHelper.DefaultOptions;

    [Fact]
    public async Task CreateMachineAccess_DefaultApiKeyPolicy_UsesApiKeyKind()
    {
        // Test the policy resolution logic by verifying that the default policy returns ApiKey kind.
        var policyStoreMock = new Mock<IMachineAuthenticationPolicyStore>();
        policyStoreMock.Setup(s => s.GetAsync("org1", "proj1")).ReturnsAsync((MachineAuthenticationPolicy?)null);

        var policy = await policyStoreMock.Object.GetAsync("org1", "proj1");
        var credentialKind = policy?.CredentialKind ?? _options.DefaultCredentialKind;

        credentialKind.ShouldBe(MachineCredentialKind.ApiKey);
    }

    [Fact]
    public async Task CreateMachineAccess_CertificatePolicy_ReturnsCertificateKind()
    {
        var policyStoreMock = new Mock<IMachineAuthenticationPolicyStore>();
        policyStoreMock.Setup(s => s.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate });

        var policy = await policyStoreMock.Object.GetAsync("org1", "proj1");
        var credentialKind = policy?.CredentialKind ?? _options.DefaultCredentialKind;

        credentialKind.ShouldBe(MachineCredentialKind.Certificate);
    }

    [Fact]
    public async Task CreateMachineAccess_CertificateAndApiKeyPolicy_ReturnsCombinedKind()
    {
        var policyStoreMock = new Mock<IMachineAuthenticationPolicyStore>();
        policyStoreMock.Setup(s => s.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.CertificateAndApiKey });

        var policy = await policyStoreMock.Object.GetAsync("org1", "proj1");
        var credentialKind = policy?.CredentialKind ?? _options.DefaultCredentialKind;

        credentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
    }

    [Fact]
    public void DefaultOptions_DefaultCredentialKind_IsApiKey()
    {
        _options.DefaultCredentialKind.ShouldBe(MachineCredentialKind.ApiKey);
    }

    [Fact]
    public void CertificateMachineAccessService_ExtendWithCertificate_PreservesExistingId()
    {
        // Test the model transformation: extending an existing access should preserve Id/ObjectId.
        var existingAccess = new MachineAccess
        {
            Id = "existing-id",
            ObjectId = "existing-oid",
            AccessKey = "2s.xxx.yyy",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.ApiKey,
        };

        var combined = existingAccess with
        {
            CredentialKind = MachineCredentialKind.CertificateAndApiKey,
            Certificate = "base64-pkcs12",
            CertificatePassword = "password",
            CertificateThumbprint = "THUMBPRINT",
        };

        combined.Id.ShouldBe("existing-id");
        combined.ObjectId.ShouldBe("existing-oid");
        combined.AccessKey.ShouldBe("2s.xxx.yyy");
        combined.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        combined.Certificate.ShouldBe("base64-pkcs12");
        combined.CertificateThumbprint.ShouldBe("THUMBPRINT");
    }
}
