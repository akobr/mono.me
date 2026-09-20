using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class LocalCertificateAuthorityProviderTests
{
    private readonly MachineAuthenticationOptions _options = TestCertificateHelper.DefaultOptions;

    [Fact]
    public async Task GetActiveAsync_EmptyStore_BootstrapsCA()
    {
        var store = new ConfigurationCertificateAuthorityStore();
        var provider = new LocalCertificateAuthorityProvider(
            store,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthorityProvider>.Instance);

        var material = await provider.GetActiveAsync();

        material.ShouldNotBeNull();
        material.Certificate.ShouldNotBeNull();
        material.Certificate.Subject.ShouldContain("2S Platform Machine CA");
        material.SignatureGenerator.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetActiveAsync_ExistingStore_LoadsWithoutBootstrap()
    {
        var store = new ConfigurationCertificateAuthorityStore();

        // Bootstrap first.
        var provider1 = new LocalCertificateAuthorityProvider(
            store,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthorityProvider>.Instance);
        var material1 = await provider1.GetActiveAsync();

        // Second provider — should load, not bootstrap.
        var provider2 = new LocalCertificateAuthorityProvider(
            store,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthorityProvider>.Instance);
        var material2 = await provider2.GetActiveAsync();

        material2.Certificate.Thumbprint.ShouldBe(material1.Certificate.Thumbprint);
    }

    [Fact]
    public async Task GetActiveAsync_AutoBootstrapDisabled_Throws()
    {
        var store = new ConfigurationCertificateAuthorityStore();
        var opts = TestCertificateHelper.DefaultOptions;
        opts.Authority.IsAutoBootstrapEnabled = false;

        var provider = new LocalCertificateAuthorityProvider(
            store,
            Options.Create(opts),
            NullLogger<LocalCertificateAuthorityProvider>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetActiveAsync());
    }

    [Fact]
    public async Task GetTrustedAsync_ReturnsAllCertificates()
    {
        var store = new ConfigurationCertificateAuthorityStore();
        var provider = new LocalCertificateAuthorityProvider(
            store,
            Options.Create(_options),
            NullLogger<LocalCertificateAuthorityProvider>.Instance);

        // Trigger bootstrap.
        await provider.GetActiveAsync();

        var trusted = await provider.GetTrustedAsync();
        trusted.Count.ShouldBeGreaterThan(0);
    }
}
