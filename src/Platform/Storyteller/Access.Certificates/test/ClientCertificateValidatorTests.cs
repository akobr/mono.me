using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class ClientCertificateValidatorTests
{
    private const string TrustDomain = TestCertificateHelper.TrustDomain;
    private readonly MachineAuthenticationOptions _options = TestCertificateHelper.DefaultOptions;
    private readonly (X509Certificate2 CaCert, X509SignatureGenerator Generator, RSA CaKey) _ca;

    public ClientCertificateValidatorTests()
    {
        _ca = TestCertificateHelper.CreateCa();
    }

    private ClientCertificateValidator CreateValidator(
        CertificateRecord? record = null,
        X509Certificate2? trustedCa = null)
    {
        var caProviderMock = new Mock<ICertificateAuthorityProvider>();
        caProviderMock.Setup(p => p.GetTrustedAsync())
            .ReturnsAsync(new List<X509Certificate2> { trustedCa ?? _ca.CaCert });

        var storeMock = new Mock<IClientCertificateStore>();
        if (record is not null)
        {
            storeMock.Setup(s => s.GetByMachineAccessIdAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(record);
        }

        return new ClientCertificateValidator(
            caProviderMock.Object,
            storeMock.Object,
            Options.Create(_options),
            NullLogger<ClientCertificateValidator>.Instance);
    }

    [Fact]
    public async Task ValidateAsync_ValidMachineCert_ReturnsResult()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var record = new CertificateRecord("m1", leaf.Thumbprint, null, null, "serial", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365), false, MachineAccessScope.DefaultRead, null, null);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);

        result.ShouldNotBeNull();
        result.Organization.ShouldBe("org1");
        result.Project.ShouldBe("proj1");
        result.MachineAccessId.ShouldBe("m1");
        result.Kind.ShouldBe(ClientCertificateKind.Machine);
        result.Scope.ShouldBe(MachineAccessScope.DefaultRead);
    }

    [Fact]
    public async Task ValidateAsync_ValidSharedCert_ReturnsSharedResult()
    {
        var leaf = TestCertificateHelper.IssueSharedLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "svc");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(leaf);

        result.ShouldNotBeNull();
        result.Kind.ShouldBe(ClientCertificateKind.Shared);
        result.Organization.ShouldBe("org1");
        result.Project.ShouldBe("proj1");
        result.MachineAccessId.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_ForeignCaCert_ReturnsNull()
    {
        var (foreignCa, foreignGen, _) = TestCertificateHelper.CreateCa();
        var leaf = TestCertificateHelper.IssueMachineLeaf(foreignCa, foreignGen, "org1", "proj1", "m1");
        var record = new CertificateRecord("m1", leaf.Thumbprint, null, null, "serial", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365), false, MachineAccessScope.DefaultRead, null, null);
        var validator = CreateValidator(record); // validator trusts _ca, not foreignCa

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredCert_ReturnsNull()
    {
        var leaf = TestCertificateHelper.IssueLeafWithCustomSan(
            _ca.CaCert, _ca.Generator,
            $"spiffe://{TrustDomain}/org/org1/project/proj1/machine/m1",
            notBefore: DateTimeOffset.UtcNow.AddDays(-400),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        var record = new CertificateRecord("m1", leaf.Thumbprint, null, null, "serial",
            DateTimeOffset.UtcNow.AddDays(-400), DateTimeOffset.UtcNow.AddDays(-1),
            false, MachineAccessScope.DefaultRead, null, null);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_MissingSan_ReturnsNull()
    {
        var leaf = TestCertificateHelper.IssueLeafWithCustomSan(_ca.CaCert, _ca.Generator, sanUri: null);
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_ThumbprintMismatch_ReturnsNull()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var record = new CertificateRecord("m1", "WRONG_THUMBPRINT", null, null, "serial", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365), false, MachineAccessScope.DefaultRead, null, null);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_RevokedRecord_ReturnsNull()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var record = new CertificateRecord("m1", leaf.Thumbprint, null, null, "serial", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365), true, MachineAccessScope.DefaultRead, null, null);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_NoRecord_ReturnsNull()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var validator = CreateValidator(record: null);

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_PreviousThumbprintDuringOverlap_Succeeds()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var record = new CertificateRecord(
            "m1",
            "NEW_THUMBPRINT",
            leaf.Thumbprint,
            DateTimeOffset.UtcNow.AddDays(7), // overlap still valid
            "serial",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365),
            false,
            MachineAccessScope.DefaultRead,
            null,
            DateTimeOffset.UtcNow);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);
        result.ShouldNotBeNull();
        result.MachineAccessId.ShouldBe("m1");
    }

    [Fact]
    public async Task ValidateAsync_PreviousThumbprintAfterOverlapExpiry_ReturnsNull()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var record = new CertificateRecord(
            "m1",
            "NEW_THUMBPRINT",
            leaf.Thumbprint,
            DateTimeOffset.UtcNow.AddDays(-1), // overlap expired
            "serial",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365),
            false,
            MachineAccessScope.DefaultRead,
            null,
            DateTimeOffset.UtcNow);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateAsync_AnnotationKey_CarriedInResult()
    {
        var leaf = TestCertificateHelper.IssueMachineLeaf(_ca.CaCert, _ca.Generator, "org1", "proj1", "m1");
        var record = new CertificateRecord("m1", leaf.Thumbprint, null, null, "serial", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365), false, MachineAccessScope.AnnotationRead, "my-annotation-key", null);
        var validator = CreateValidator(record);

        var result = await validator.ValidateAsync(leaf);

        result.ShouldNotBeNull();
        result.AnnotationKey.ShouldBe("my-annotation-key");
        result.Scope.ShouldBe(MachineAccessScope.AnnotationRead);
    }
}
