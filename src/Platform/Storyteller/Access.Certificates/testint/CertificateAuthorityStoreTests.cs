using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public class CertificateAuthorityStoreTests(CosmosFixture fixture)
{
    [Fact]
    public async Task Bootstrap_StoresAndRetrievesCA()
    {
        var caProvider = fixture.Services.GetRequiredService<ICertificateAuthorityProvider>();

        var material = await caProvider.GetActiveAsync();

        material.ShouldNotBeNull();
        material.Certificate.ShouldNotBeNull();
        material.Certificate.Subject.ShouldContain("2S Platform Machine CA");
        material.SignatureGenerator.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetActiveRecordAsync_ReturnsStoredCA()
    {
        // Ensure the CA is bootstrapped first.
        var caProvider = fixture.Services.GetRequiredService<ICertificateAuthorityProvider>();
        await caProvider.GetActiveAsync();

        var store = fixture.Services.GetRequiredService<ICertificateAuthorityStore>();
        var record = await store.GetActiveRecordAsync();

        record.ShouldNotBeNull();
        record!.CertificateData.ShouldNotBeEmpty();
        record.Pkcs12Data.ShouldNotBeNull();
        record.Version.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAllCertificatesAsync_ReturnsAtLeastOne()
    {
        var caProvider = fixture.Services.GetRequiredService<ICertificateAuthorityProvider>();
        await caProvider.GetActiveAsync();

        var store = fixture.Services.GetRequiredService<ICertificateAuthorityStore>();
        var certificates = await store.GetAllCertificatesAsync();

        certificates.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GetTrustedAsync_ReturnsBootstrappedCA()
    {
        var caProvider = fixture.Services.GetRequiredService<ICertificateAuthorityProvider>();
        await caProvider.GetActiveAsync();

        var trusted = await caProvider.GetTrustedAsync();

        trusted.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task StoreCertificateAsync_ConditionalCreate_DoesNotThrowOnConflict()
    {
        var caProvider = fixture.Services.GetRequiredService<ICertificateAuthorityProvider>();
        var material = await caProvider.GetActiveAsync();

        var store = fixture.Services.GetRequiredService<ICertificateAuthorityStore>();

        // Attempt to store a certificate with the same version — should not throw (conflict swallowed).
        await store.StoreCertificateAsync(material.Certificate.RawData, "1", null);
    }
}
