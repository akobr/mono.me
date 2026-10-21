using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// A machine picks one of the kinds its project enables; a certificate gets the project's default lifetime.
public class PolicyAwareCredentialKindTests
{
    private static readonly DateTimeOffset NotAfter = new(2027, 1, 12, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMachineAuthenticationPolicyStore> _policyStore = new();
    private readonly Mock<IApiKeyHashStore> _hashStore = new();
    private readonly Mock<IClientCertificateAuthority> _authority = new();
    private readonly Mock<IClientCertificateStore> _certificateStore = new();
    private readonly List<CertificateRecord> _records = [];
    private readonly List<int> _lifetimes = [];

    public PolicyAwareCredentialKindTests()
    {
        _authority
            .Setup(authority => authority.IssueCertificateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback((string _, string _, string _, int days) => _lifetimes.Add(days))
            .ReturnsAsync(new IssuedCertificate([1, 2, 3], "password", "THUMBPRINT", "01", NotAfter.AddDays(-90), NotAfter));
        _certificateStore
            .Setup(store => store.StoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CertificateRecord>()))
            .Callback((string _, string _, CertificateRecord record) => _records.Add(record))
            .Returns(Task.CompletedTask);
        SetPolicy(new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate, MachineCredentialKind.CertificateAndApiKey],
            CredentialKind = MachineCredentialKind.Certificate,
            CertificateLifetimeDays = 90,
        });
    }

    [Fact]
    public async Task Create_WithoutAKind_UsesTheProjectDefaultKindAndLifetime()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model());

        access.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        access.CertificateLifetimeDays.ShouldBe(90);
        access.CertificateNotAfter.ShouldBe(NotAfter);
        _lifetimes.ShouldBe([90]);
        _records.ShouldHaveSingleItem().CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
    }

    [Fact]
    public async Task Create_AnEnabledKind_IsUsedInsteadOfTheDefault()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey));

        access.CredentialKind.ShouldBe(MachineCredentialKind.ApiKey);
        access.AccessKey.ShouldStartWith("2s.");
        access.CertificateLifetimeDays.ShouldBeNull();
        _authority.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_CertificateAndApiKey_RecordsTheCombinedKind()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey));

        access.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        access.CertificateLifetimeDays.ShouldBe(90);
        _records.ShouldHaveSingleItem().CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
    }

    [Fact]
    public async Task Create_ARequestedLifetime_WinsOverTheProjectDefault()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.Certificate, lifetimeDays: 30));

        access.CertificateLifetimeDays.ShouldBe(30);
        _lifetimes.ShouldBe([30]);
    }

    [Fact]
    public async Task Create_ALifetimeOutOfBounds_IsClamped()
    {
        await CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.Certificate, lifetimeDays: 99_999));

        _lifetimes.ShouldBe([3650]);
    }

    [Fact]
    public async Task Create_AKindTheProjectDoesNotEnable_IsNotSupported()
    {
        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateService().CreateMachineAccessAsync(Model(MachineCredentialKind.ClientCredentials)));

        exception.Message.ShouldBe("The credential kind ClientCredentials is not enabled in the project org1.proj1.");
        _hashStore.VerifyNoOtherCalls();
        _authority.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_WithoutAProjectPolicy_EnablesOnlyTheDeploymentDefault()
    {
        SetPolicy(null);
        var service = CreateService();

        (await service.CreateMachineAccessAsync(Model())).CredentialKind.ShouldBe(MachineCredentialKind.ApiKey);
        await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(Model(MachineCredentialKind.Certificate)));
    }

    [Fact]
    public async Task Create_LegacyCertificatePolicy_UsesItsLifetime()
    {
        SetPolicy(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate, CertificateLifetimeDays = 45 });

        var access = await CreateService().CreateMachineAccessAsync(Model());

        access.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        _lifetimes.ShouldBe([45]);
    }

    [Fact]
    public async Task Create_CertificateAndApiKeyWithASharedCertificate_IssuesNoCertificate()
    {
        var access = await CreateService().CreateMachineAccessAsync(
            Model(MachineCredentialKind.CertificateAndApiKey, lifetimeDays: 30, useSharedCertificate: true));

        access.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        access.UsesSharedCertificate.ShouldBeTrue();
        access.AccessKey.ShouldStartWith("2s.");
        access.Certificate.ShouldBeNull();
        access.CertificatePassword.ShouldBeNull();
        access.CertificateThumbprint.ShouldBeNull();
        access.CertificateLifetimeDays.ShouldBeNull();
        access.CertificateNotAfter.ShouldBeNull();
        _authority.VerifyNoOtherCalls();
        _records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_ASharedCertificateWithTheDefaultCertificateAndApiKeyKind_IssuesNoCertificate()
    {
        SetPolicy(new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.CertificateAndApiKey],
            CredentialKind = MachineCredentialKind.CertificateAndApiKey,
        });

        var access = await CreateService().CreateMachineAccessAsync(Model(useSharedCertificate: true));

        access.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        access.UsesSharedCertificate.ShouldBeTrue();
        _authority.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(null)]
    public async Task Create_ASharedCertificateWithAnotherKind_IsNotSupported(MachineCredentialKind? kind)
    {
        // the project default is Certificate
        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateService().CreateMachineAccessAsync(Model(kind, useSharedCertificate: true)));

        exception.Message.ShouldStartWith("Only CertificateAndApiKey machines can use a shared certificate");
        _hashStore.VerifyNoOtherCalls();
        _authority.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_CertificateAndApiKeyWithoutASharedCertificate_IssuesItsOwn()
    {
        var access = await CreateService().CreateMachineAccessAsync(
            Model(MachineCredentialKind.CertificateAndApiKey, useSharedCertificate: false));

        access.UsesSharedCertificate.ShouldBeFalse();
        access.CertificateThumbprint.ShouldBe("THUMBPRINT");
        _lifetimes.ShouldBe([90]);
    }

    private static MachineAccessCreate Model(MachineCredentialKind? kind = null, int? lifetimeDays = null, bool? useSharedCertificate = null)
    {
        return new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            CredentialKind = kind,
            CertificateLifetimeDays = lifetimeDays,
            UseSharedCertificate = useSharedCertificate,
        };
    }

    private void SetPolicy(MachineAuthenticationPolicy? policy)
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1")).ReturnsAsync(policy);
    }

    private PolicyAwareMachineAccessService CreateService()
    {
        var options = Options.Create(TestCertificateHelper.DefaultOptions);

        return new PolicyAwareMachineAccessService(
            new ApiKeyMachineAccessService(_hashStore.Object),
            new CertificateMachineAccessService(_authority.Object, _certificateStore.Object, options, NullLogger<CertificateMachineAccessService>.Instance),
            _policyStore.Object,
            options,
            NullLogger<PolicyAwareMachineAccessService>.Instance);
    }
}
