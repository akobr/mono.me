using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// Federated routing. The API key and certificate services are null here, so reaching them fails the test.
public class PolicyAwareFederatedTests
{
    private readonly Mock<IMachineAuthenticationPolicyStore> _policyStore = new();
    private readonly Mock<IFederatedMachineAccessService> _federated = new();

    public PolicyAwareFederatedTests()
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1")).ReturnsAsync(new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Federated],
            CredentialKind = MachineCredentialKind.Federated,
        });
    }

    [Fact]
    public async Task Create_Federated_IsRoutedToTheFederatedServiceWithItsRules()
    {
        _federated.Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .ReturnsAsync(Access(MachineCredentialKind.ApiKey));

        var access = await CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.Federated, [Rule()]) with
        {
            CertificateLifetimeDays = 30,
        });

        access.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        _federated.Verify(
            service => service.CreateMachineAccessAsync(It.Is<MachineAccessCreate>(model =>
                model.CredentialKind == MachineCredentialKind.Federated
                && model.FederationRules!.Single() == Rule()
                && model.CertificateLifetimeDays == null
                && model.UseSharedCertificate == false)),
            Times.Once);
    }

    [Fact]
    public async Task Create_TheProjectDefaultKind_RoutesFederatedToo()
    {
        _federated.Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .ReturnsAsync(Access(MachineCredentialKind.Federated));

        var access = await CreateService().CreateMachineAccessAsync(Model(null, [Rule()]));

        access.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        _federated.Verify(service => service.CreateMachineAccessAsync(It.Is<MachineAccessCreate>(model => model.FederationRules!.Count == 1)), Times.Once);
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    [InlineData(MachineCredentialKind.ClientCredentials)]
    public async Task Create_RulesOnAnotherKind_AreRefused(MachineCredentialKind kind)
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { EnabledCredentialKinds = [kind], CredentialKind = kind });

        var exception = await Should.ThrowAsync<InvalidInputException>(
            () => CreateService().CreateMachineAccessAsync(Model(kind, [Rule()])));

        exception.Message.ShouldBe("Only Federated machines have federation rules.");
        exception.ErrorCode.ShouldBe(ErrorCodes.MachineNotFederated);
        _federated.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_FederatedNotEnabledByTheProject_IsNotSupported()
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.Federated, [Rule()])));

        exception.Message.ShouldBe("The credential kind Federated is not enabled in the project org1.proj1.");
        _federated.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_FederatedWithoutTheService_IsNotSupported()
    {
        var exception = await Should.ThrowAsync<FeatureNotConfiguredException>(
            () => CreateService(withFederated: false).CreateMachineAccessAsync(Model(MachineCredentialKind.Federated, [Rule()])));

        exception.Message.ShouldBe("Federated machine access is not configured for this deployment.");
        exception.ErrorCode.ShouldBe(ErrorCodes.MachineAccessNotConfigured);
    }

    [Fact]
    public async Task Create_FederatedWithASharedCertificate_IsNotSupported()
    {
        await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.Federated, [Rule()]) with { UseSharedCertificate = true }));

        _federated.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResetAndDelete_FollowTheStoredKind_NeverTheApiKeyOrCertificateServices()
    {
        // The project has since moved back to API keys; the stored machine still is Federated.
        _policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });
        var existing = Access(MachineCredentialKind.Federated);
        _federated.Setup(service => service.ResetMachineAccessAsync(existing, "org1", "proj1"))
            .ThrowsAsync(new MachineAccessNotSupportedException("A Federated machine has no secret to reset. Change its federation rules instead."));
        _federated.Setup(service => service.DeleteMachineAccessAsync(existing, "org1", "proj1")).ReturnsAsync(true);
        var service = CreateService();

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.ResetMachineAccessAsync(existing, "org1", "proj1"));
        exception.Message.ShouldBe("A Federated machine has no secret to reset. Change its federation rules instead.");
        (await service.DeleteMachineAccessAsync(existing, "org1", "proj1")).ShouldBeTrue();

        _federated.Verify(service => service.DeleteMachineAccessAsync(existing, "org1", "proj1"), Times.Once);
        _policyStore.Verify(store => store.GetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResetWithoutTheService_IsNotSupported_ButDeleteStillRemovesTheMachine()
    {
        var existing = Access(MachineCredentialKind.Federated);
        var service = CreateService(withFederated: false);

        var exception = await Should.ThrowAsync<FeatureNotConfiguredException>(() => service.ResetMachineAccessAsync(existing, "org1", "proj1"));

        exception.Message.ShouldBe("Federated machine access is not configured for this deployment.");
        exception.ErrorCode.ShouldBe(ErrorCodes.MachineAccessNotConfigured);
        (await service.DeleteMachineAccessAsync(existing, "org1", "proj1")).ShouldBeTrue();
    }

    private static FederationRule Rule()
    {
        return new FederationRule { Issuer = "github", Audience = "urn:test:storyteller", Subject = "repo:acme/app:ref:refs/heads/main" };
    }

    private static MachineAccessCreate Model(MachineCredentialKind? kind, IReadOnlyList<FederationRule>? rules)
    {
        return new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            CredentialKind = kind,
            FederationRules = rules,
        };
    }

    private static MachineAccess Access(MachineCredentialKind kind)
    {
        return new MachineAccess
        {
            Id = "3b0f9d3e-8f43-4a8e-9a0c-2f7d3c1e5b6a",
            ObjectId = "3b0f9d3e-8f43-4a8e-9a0c-2f7d3c1e5b6a",
            AccessKey = string.Empty,
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            CredentialKind = kind,
            FederationRules = [Rule()],
        };
    }

    private PolicyAwareMachineAccessService CreateService(bool withFederated = true)
    {
        return new PolicyAwareMachineAccessService(
            null!,
            null!,
            _policyStore.Object,
            Options.Create(TestCertificateHelper.DefaultOptions),
            NullLogger<PolicyAwareMachineAccessService>.Instance,
            identityProviderService: null,
            federatedService: withFederated ? _federated.Object : null);
    }
}
