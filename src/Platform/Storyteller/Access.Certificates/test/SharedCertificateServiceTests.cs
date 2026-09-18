using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class SharedCertificateServiceTests
{
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
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var result = await service.IssueAsync("org1", "proj1", "my-service", 365);

        result.ShouldNotBeNull();
        result.Label.ShouldBe("my-service");
        result.Thumbprint.ShouldNotBeNullOrEmpty();
        result.Certificate.ShouldNotBeNullOrEmpty();
        result.CertificatePassword.ShouldNotBeNullOrEmpty();

        storeMock.Verify(s => s.StoreAsync("org1", "proj1", It.IsAny<SharedCertificate>()), Times.Once);
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
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var result = await service.IssueAsync("org1", "proj1", "my-service", 365);

        result.Label.ShouldStartWith("my-service-");
        result.Label.Length.ShouldBeGreaterThan("my-service".Length);
    }

    [Fact]
    public async Task RevokeAsync_DelegatesToStore()
    {
        var storeMock = new Mock<ISharedCertificateStore>();
        storeMock.Setup(s => s.RevokeAsync("org1", "proj1", "THUMBPRINT")).ReturnsAsync(true);

        var authorityMock = new Mock<IClientCertificateAuthority>();

        var service = new SharedCertificateService(
            authorityMock.Object,
            storeMock.Object,
            Options.Create(_options),
            NullLogger<SharedCertificateService>.Instance);

        var result = await service.RevokeAsync("org1", "proj1", "THUMBPRINT");

        result.ShouldBeTrue();
        storeMock.Verify(s => s.RevokeAsync("org1", "proj1", "THUMBPRINT"), Times.Once);
    }
}
