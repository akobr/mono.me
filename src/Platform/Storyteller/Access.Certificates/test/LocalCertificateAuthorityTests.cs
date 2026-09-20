using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class LocalCertificateAuthorityTests
{
    private readonly MachineAuthenticationOptions _options = TestCertificateHelper.DefaultOptions;

    private LocalCertificateAuthority CreateAuthority(CertificateAuthorityMaterial? material = null)
    {
        material ??= TestCertificateHelper.CreateCaMaterial();
        var providerMock = new Mock<ICertificateAuthorityProvider>();
        providerMock.Setup(p => p.GetActiveAsync()).ReturnsAsync(material);

        return new LocalCertificateAuthority(
            providerMock.Object,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthority>.Instance);
    }

    [Fact]
    public async Task IssueCertificate_ProducesValidLeaf()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var authority = CreateAuthority(material);

        var issued = await authority.IssueCertificateAsync("org1", "proj1", "machine-1", MachineAccessScope.DefaultRead, 365);

        issued.ShouldNotBeNull();
        issued.Pkcs12.ShouldNotBeEmpty();
        issued.Password.ShouldNotBeNullOrEmpty();
        issued.Thumbprint.ShouldNotBeNullOrEmpty();
        issued.SerialNumber.ShouldNotBeNullOrEmpty();
        issued.NotBefore.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
        issued.NotAfter.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task IssueCertificate_LeafHasSanWithCorrectSpiffeUri()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var authority = CreateAuthority(material);

        var issued = await authority.IssueCertificateAsync("org1", "proj1", "machine-1", MachineAccessScope.DefaultRead, 365);
        var cert = X509CertificateLoader.LoadPkcs12(issued.Pkcs12, issued.Password);
        var identity = CertificateIdentity.TryParse(cert, _options.TrustDomain);

        identity.ShouldNotBeNull();
        identity.Kind.ShouldBe(ClientCertificateKind.Machine);
        identity.Organization.ShouldBe("org1");
        identity.Project.ShouldBe("proj1");
        identity.MachineAccessId.ShouldBe("machine-1");
    }

    [Fact]
    public async Task IssueCertificate_LeafChainsToCA()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var authority = CreateAuthority(material);

        var issued = await authority.IssueCertificateAsync("org1", "proj1", "m1", MachineAccessScope.DefaultRead, 365);
        var cert = X509CertificateLoader.LoadPkcs12(issued.Pkcs12, issued.Password);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(material.Certificate);

        chain.Build(cert).ShouldBeTrue();
    }

    [Fact]
    public async Task IssueCertificate_LeafHasClientAuthEku()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var authority = CreateAuthority(material);

        var issued = await authority.IssueCertificateAsync("org1", "proj1", "m1", MachineAccessScope.DefaultRead, 365);
        var cert = X509CertificateLoader.LoadPkcs12(issued.Pkcs12, issued.Password);

        var ekuExt = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        ekuExt.ShouldNotBeNull();
        ekuExt.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
            .ShouldContain(oid => oid.Value == "1.3.6.1.5.5.7.3.2");
    }

    [Fact]
    public async Task IssueCertificate_ClampsLifetimeToCaValidity()
    {
        // CA with 1 year remaining
        var (caCert, generator, _) = TestCertificateHelper.CreateCa(lifetimeYears: 1);
        var material = new CertificateAuthorityMaterial(caCert, generator);
        var authority = CreateAuthority(material);

        // Request 10-year leaf — should be clamped
        var issued = await authority.IssueCertificateAsync("org1", "proj1", "m1", MachineAccessScope.DefaultRead, 3650);

        var daysDiff = (issued.NotAfter - issued.NotBefore).TotalDays;
        daysDiff.ShouldBeLessThan(366); // clamped to ~365
    }

    [Fact]
    public async Task IssueSharedCertificate_HasSharedSan()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var authority = CreateAuthority(material);

        var issued = await authority.IssueSharedCertificateAsync("org1", "proj1", "my-service", 365);
        var cert = X509CertificateLoader.LoadPkcs12(issued.Pkcs12, issued.Password);
        var identity = CertificateIdentity.TryParse(cert, _options.TrustDomain);

        identity.ShouldNotBeNull();
        identity.Kind.ShouldBe(ClientCertificateKind.Shared);
        identity.SharedLabel.ShouldBe("my-service");
    }

    [Fact]
    public async Task IssueCertificate_Pkcs12CanBeLoaded()
    {
        var material = TestCertificateHelper.CreateCaMaterial();
        var authority = CreateAuthority(material);

        var issued = await authority.IssueCertificateAsync("org1", "proj1", "m1", MachineAccessScope.DefaultRead, 365);

        var cert = X509CertificateLoader.LoadPkcs12(issued.Pkcs12, issued.Password);
        cert.ShouldNotBeNull();
        cert.HasPrivateKey.ShouldBeTrue();
    }
}
