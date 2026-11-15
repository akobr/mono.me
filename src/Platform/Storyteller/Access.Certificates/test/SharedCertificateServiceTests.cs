using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class SharedCertificateServiceTests
{
    // The administrator who acts, with the invocation of the request: the store records both with the change.
    private static readonly AccessCaller Caller = AccessCaller.Account("admin-1", "Ada Admin") with { CorrelationId = "invocation-1" };

    private readonly MachineAuthenticationOptions _options = TestCertificateHelper.DefaultOptions;

    [Fact]
    public async Task IssueAsync_ReturnsValidCertificate()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var caProviderMock = new Mock<ICertificateAuthorityProvider>();
        caProviderMock.Setup(p => p.GetActiveAsync()).ReturnsAsync(material);

        var authority = new LocalCertificateAuthority(
            caProviderMock.Object,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthority>.Instance);

        var storeMock = new Mock<ISharedCertificateStore>();
        storeMock.Setup(s => s.LabelExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var service = new SharedCertificateService(
            authority,
            storeMock.Object,
            PolicyStore(MachineCredentialKind.CertificateAndApiKey),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var result = await service.IssueAsync("org1", "proj1", "my-service", 365, Caller);

        result.ShouldNotBeNull();
        result.Label.ShouldBe("my-service");
        result.Thumbprint.ShouldNotBeNullOrEmpty();
        result.Certificate.ShouldNotBeNullOrEmpty();
        result.CertificatePassword.ShouldNotBeNullOrEmpty();

        storeMock.Verify(s => s.StoreAsync("org1", "proj1", It.IsAny<SharedCertificate>(), Caller), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_DuplicateLabel_AppendsTimestamp()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var caProviderMock = new Mock<ICertificateAuthorityProvider>();
        caProviderMock.Setup(p => p.GetActiveAsync()).ReturnsAsync(material);

        var authority = new LocalCertificateAuthority(
            caProviderMock.Object,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthority>.Instance);

        var storeMock = new Mock<ISharedCertificateStore>();
        storeMock.Setup(s => s.LabelExistsAsync("org1", "proj1", "my-service"))
            .ReturnsAsync(true);

        var service = new SharedCertificateService(
            authority,
            storeMock.Object,
            PolicyStore(MachineCredentialKind.CertificateAndApiKey),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var result = await service.IssueAsync("org1", "proj1", "my-service", 365, Caller);

        result.Label.ShouldStartWith("my-service-");
        result.Label.Length.ShouldBeGreaterThan("my-service".Length);
    }

    [Fact]
    public async Task RevokeAsync_DelegatesToStore()
    {
        var storeMock = new Mock<ISharedCertificateStore>();
        storeMock.Setup(s => s.RevokeAsync("org1", "proj1", "THUMBPRINT", Caller)).ReturnsAsync(true);

        var authorityMock = new Mock<IClientCertificateAuthority>();

        var service = new SharedCertificateService(
            authorityMock.Object,
            storeMock.Object,
            PolicyStore(MachineCredentialKind.CertificateAndApiKey),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var result = await service.RevokeAsync("org1", "proj1", "THUMBPRINT", Caller);

        result.ShouldBeTrue();
        storeMock.Verify(s => s.RevokeAsync("org1", "proj1", "THUMBPRINT", Caller), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_AProjectWithoutCertificateAndApiKey_IsNotSupported()
    {
        var storeMock = new Mock<ISharedCertificateStore>();
        var authorityMock = new Mock<IClientCertificateAuthority>();

        var service = new SharedCertificateService(
            authorityMock.Object,
            storeMock.Object,
            PolicyStore(MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => service.IssueAsync("org1", "proj1", "my-service", 365, Caller));

        exception.Message.ShouldBe("The project org1.proj1 does not enable CertificateAndApiKey, which shared certificates belong to.");
        authorityMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IssueAsync_CertificateAndApiKeyTurnedOffByTheDeployment_IsNotSupported()
    {
        var storeMock = new Mock<ISharedCertificateStore>();
        var authorityMock = new Mock<IClientCertificateAuthority>();
        var options = TestCertificateHelper.DefaultOptions;
        options.DisabledCredentialKinds = [MachineCredentialKind.Certificate, MachineCredentialKind.CertificateAndApiKey];

        var service = new SharedCertificateService(
            authorityMock.Object,
            storeMock.Object,
            PolicyStore(MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey),
            Options.Create(options),
            NullLogger<SharedCertificateService>.Instance);

        await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => service.IssueAsync("org1", "proj1", "my-service", 365, Caller));

        authorityMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeAsync_AProjectWithoutCertificateAndApiKey_StillRevokes()
    {
        var storeMock = new Mock<ISharedCertificateStore>();
        storeMock.Setup(s => s.RevokeAsync("org1", "proj1", "THUMBPRINT", Caller)).ReturnsAsync(true);

        var service = new SharedCertificateService(
            new Mock<IClientCertificateAuthority>().Object,
            storeMock.Object,
            PolicyStore(MachineCredentialKind.ApiKey),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        (await service.RevokeAsync("org1", "proj1", "THUMBPRINT", Caller)).ShouldBeTrue();
    }

    // The store records the change with the caller it is given, so the service hands it on as it got it.
    [Fact]
    public async Task IssueAsync_PassesTheCallerToTheStore()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var caProviderMock = new Mock<ICertificateAuthorityProvider>();
        caProviderMock.Setup(p => p.GetActiveAsync()).ReturnsAsync(material);
        var store = new RecordingSharedCertificateStore();

        var service = new SharedCertificateService(
            new LocalCertificateAuthority(caProviderMock.Object, Options.Create(_options), NullLogger<LocalCertificateAuthority>.Instance),
            store,
            PolicyStore(MachineCredentialKind.CertificateAndApiKey),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var issued = await service.IssueAsync("org1", "proj1", "my-service", 365, Caller);

        var stored = store.Stored.ShouldHaveSingleItem();
        stored.Caller.ShouldBeSameAs(Caller);
        stored.Certificate.Thumbprint.ShouldBe(issued.Thumbprint);
    }

    [Fact]
    public async Task RevokeAsync_PassesTheCallerToTheStore()
    {
        var store = new RecordingSharedCertificateStore();

        var service = new SharedCertificateService(
            new Mock<IClientCertificateAuthority>().Object,
            store,
            PolicyStore(MachineCredentialKind.CertificateAndApiKey),
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        (await service.RevokeAsync("org1", "proj1", "THUMBPRINT", AccessCaller.System)).ShouldBeTrue();

        var revoked = store.Revoked.ShouldHaveSingleItem();
        revoked.Thumbprint.ShouldBe("THUMBPRINT");
        revoked.Caller.ShouldBeSameAs(AccessCaller.System);
    }

    private static IMachineAuthenticationPolicyStore PolicyStore(params MachineCredentialKind[] kinds)
    {
        var policyStore = new Mock<IMachineAuthenticationPolicyStore>();
        policyStore.Setup(store => store.GetAsync("org1", "proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy { EnabledCredentialKinds = kinds });
        return policyStore.Object;
    }

    // Keeps what it was asked to store and revoke, with the caller of each.
    private sealed class RecordingSharedCertificateStore : ISharedCertificateStore
    {
        public List<(SharedCertificate Certificate, AccessCaller Caller)> Stored { get; } = [];

        public List<(string Thumbprint, AccessCaller Caller)> Revoked { get; } = [];

        public Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project)
        {
            return Task.FromResult<IReadOnlyList<SharedCertificate>>(Stored.Select(stored => stored.Certificate).ToList());
        }

        public Task<SharedCertificate?> GetByThumbprintAsync(string organization, string project, string thumbprint)
        {
            return Task.FromResult(Stored.Select(stored => stored.Certificate).FirstOrDefault(certificate => certificate.Thumbprint == thumbprint));
        }

        public Task StoreAsync(string organization, string project, SharedCertificate certificate, AccessCaller caller)
        {
            Stored.Add((certificate, caller));
            return Task.CompletedTask;
        }

        public Task<bool> RevokeAsync(string organization, string project, string thumbprint, AccessCaller caller)
        {
            Revoked.Add((thumbprint, caller));
            return Task.FromResult(true);
        }

        public Task<bool> LabelExistsAsync(string organization, string project, string label)
        {
            return Task.FromResult(false);
        }
    }
}
