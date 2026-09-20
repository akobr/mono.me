using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class CertificateIdentityTests
{
    private const string TrustDomain = TestCertificateHelper.TrustDomain;

    [Fact]
    public void ToSpiffeUri_Machine_ProducesExpectedUri()
    {
        var identity = new CertificateIdentity("myorg", "myproj", "machine-1", null, ClientCertificateKind.Machine);
        var uri = identity.ToSpiffeUri(TrustDomain);
        uri.ShouldBe($"spiffe://{TrustDomain}/org/myorg/project/myproj/machine/machine-1");
    }

    [Fact]
    public void ToSpiffeUri_Shared_ProducesExpectedUri()
    {
        var identity = new CertificateIdentity("myorg", "myproj", null, "my-label", ClientCertificateKind.Shared);
        var uri = identity.ToSpiffeUri(TrustDomain);
        uri.ShouldBe($"spiffe://{TrustDomain}/org/myorg/project/myproj/shared/my-label");
    }

    [Fact]
    public void SanRoundTrip_Machine_ParsesCorrectly()
    {
        var (caCert, generator, _) = TestCertificateHelper.CreateCa();
        var leaf = TestCertificateHelper.IssueMachineLeaf(caCert, generator, "org1", "proj1", "machine-abc");

        var parsed = CertificateIdentity.TryParse(leaf, TrustDomain);

        parsed.ShouldNotBeNull();
        parsed!.Kind.ShouldBe(ClientCertificateKind.Machine);
        parsed.Organization.ShouldBe("org1");
        parsed.Project.ShouldBe("proj1");
        parsed.MachineAccessId.ShouldBe("machine-abc");
        parsed.SharedLabel.ShouldBeNull();
    }

    [Fact]
    public void SanRoundTrip_Shared_ParsesCorrectly()
    {
        var (caCert, generator, _) = TestCertificateHelper.CreateCa();
        var leaf = TestCertificateHelper.IssueSharedLeaf(caCert, generator, "org1", "proj1", "svc-label");

        var parsed = CertificateIdentity.TryParse(leaf, TrustDomain);

        parsed.ShouldNotBeNull();
        parsed!.Kind.ShouldBe(ClientCertificateKind.Shared);
        parsed.Organization.ShouldBe("org1");
        parsed.Project.ShouldBe("proj1");
        parsed.SharedLabel.ShouldBe("svc-label");
        parsed.MachineAccessId.ShouldBeNull();
    }

    [Fact]
    public void TryParse_MissingSan_ReturnsNull()
    {
        var (caCert, generator, _) = TestCertificateHelper.CreateCa();
        var leaf = TestCertificateHelper.IssueLeafWithCustomSan(caCert, generator, sanUri: null);

        var parsed = CertificateIdentity.TryParse(leaf, TrustDomain);
        parsed.ShouldBeNull();
    }

    [Fact]
    public void TryParse_WrongTrustDomain_ReturnsNull()
    {
        var (caCert, generator, _) = TestCertificateHelper.CreateCa();
        var leaf = TestCertificateHelper.IssueMachineLeaf(caCert, generator, "org1", "proj1", "m1");

        var parsed = CertificateIdentity.TryParse(leaf, "wrong.domain");
        parsed.ShouldBeNull();
    }

    [Theory]
    [InlineData("valid-name")]
    [InlineData("abc123")]
    [InlineData("my.org.name")]
    [InlineData("with_underscore")]
    [InlineData("with-dash")]
    public void IsValidSpiffePathSegment_ValidValues_ReturnsTrue(string value)
    {
        CertificateIdentity.IsValidSpiffePathSegment(value).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("with/slash")]
    [InlineData("with:colon")]
    [InlineData("with@at")]
    public void IsValidSpiffePathSegment_InvalidValues_ReturnsFalse(string value)
    {
        CertificateIdentity.IsValidSpiffePathSegment(value).ShouldBeFalse();
    }
}
