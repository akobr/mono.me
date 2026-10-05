using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// ClientCredentials routing. The API key and certificate services are not reached on these paths.
public class PolicyAwareClientCredentialsTests
{
    private readonly Mock<IMachineAuthenticationPolicyStore> _policyStore = new();
    private readonly Mock<IIdentityProviderMachineAccessService> _identityProvider = new();

    [Fact]
    public async Task Create_ClientCredentialsPolicy_IsRoutedToTheIdentityProviderAndStamped()
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ClientCredentials });
        _identityProvider.Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .ReturnsAsync(Access(MachineCredentialKind.ApiKey));

        var access = await CreateService().CreateMachineAccessAsync(Model());

        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        access.Id.ShouldBe("client_m2m01");
        _identityProvider.Verify(service => service.CreateMachineAccessAsync(It.Is<MachineAccessCreate>(model => model.Project == "proj1")), Times.Once);
    }

    [Fact]
    public async Task Create_ClientCredentialsDefault_IsRoutedToo()
    {
        _identityProvider.Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .ReturnsAsync(Access(MachineCredentialKind.ClientCredentials));

        var access = await CreateService(defaultKind: MachineCredentialKind.ClientCredentials).CreateMachineAccessAsync(Model());

        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
    }

    [Fact]
    public async Task Create_ClientCredentialsWithoutIdentityProvider_IsNotSupported()
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ClientCredentials });

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateService(withIdentityProvider: false).CreateMachineAccessAsync(Model()));

        exception.Message.ShouldBe("ClientCredentials machine access is not configured for this deployment.");
    }

    [Fact]
    public async Task ResetAndDelete_FollowTheStoredKindNotTheCurrentPolicy()
    {
        // The project has since moved to API keys; the stored access still is an M2M application.
        _policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });
        var existing = Access(MachineCredentialKind.ClientCredentials);
        _identityProvider.Setup(service => service.ResetMachineAccessAsync(existing, "org1", "proj1")).ReturnsAsync("new-secret");
        _identityProvider.Setup(service => service.DeleteMachineAccessAsync(existing, "org1", "proj1")).ReturnsAsync(true);
        var service = CreateService();

        (await service.ResetMachineAccessAsync(existing, "org1", "proj1")).ShouldBe("new-secret");
        (await service.DeleteMachineAccessAsync(existing, "org1", "proj1")).ShouldBeTrue();

        _policyStore.Verify(store => store.GetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResetOfAnM2MAccess_WithoutIdentityProvider_IsNotSupported()
    {
        await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateService(withIdentityProvider: false).ResetMachineAccessAsync(Access(MachineCredentialKind.ClientCredentials), "org1", "proj1"));
    }

    private static MachineAccessCreate Model()
    {
        return new MachineAccessCreate { Organization = "org1", Project = "proj1", Scope = MachineAccessScope.DefaultRead };
    }

    private static MachineAccess Access(MachineCredentialKind kind)
    {
        return new MachineAccess
        {
            Id = "client_m2m01",
            ObjectId = "conn_app_01",
            AccessKey = "secret",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = kind,
        };
    }

    private PolicyAwareMachineAccessService CreateService(
        bool withIdentityProvider = true,
        MachineCredentialKind defaultKind = MachineCredentialKind.ApiKey)
    {
        var options = TestCertificateHelper.DefaultOptions;
        options.DefaultCredentialKind = defaultKind;

        return new PolicyAwareMachineAccessService(
            null!,
            null!,
            _policyStore.Object,
            Options.Create(options),
            NullLogger<PolicyAwareMachineAccessService>.Instance,
            withIdentityProvider ? _identityProvider.Object : null);
    }
}
