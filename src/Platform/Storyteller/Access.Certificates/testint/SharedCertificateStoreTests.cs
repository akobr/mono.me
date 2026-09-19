using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// Tests shared certificate store operations against real Cosmos.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SharedCertificateStoreTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "sharedproj";

    [Fact]
    public async Task IssueAndList_SharedCertificate()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var cert = await service.IssueAsync(Organization, Project, "my-service", 365);

        cert.ShouldNotBeNull();
        cert.Label.ShouldBe("my-service");
        cert.Thumbprint.ShouldNotBeNullOrEmpty();
        cert.Certificate.ShouldNotBeNullOrEmpty();
        cert.CertificatePassword.ShouldNotBeNullOrEmpty();
        cert.NotAfter.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        // List should include the issued cert.
        var list = await service.ListAsync(Organization, Project);
        list.ShouldContain(c => c.Thumbprint == cert.Thumbprint);
    }

    [Fact]
    public async Task Revoke_SharedCertificate()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var cert = await service.IssueAsync(Organization, Project, "to-revoke", 365);
        var revoked = await service.RevokeAsync(Organization, Project, cert.Thumbprint);

        revoked.ShouldBeTrue();

        var list = await service.ListAsync(Organization, Project);
        var revokedCert = list.FirstOrDefault(c => c.Thumbprint == cert.Thumbprint);
        revokedCert.ShouldNotBeNull();
        revokedCert.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoke_NonExistent_ReturnsFalse()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var result = await service.RevokeAsync(Organization, Project, "NONEXISTENT_THUMBPRINT");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DuplicateLabel_AppendsSuffix()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var cert1 = await service.IssueAsync(Organization, Project, "dup-label", 365);
        var cert2 = await service.IssueAsync(Organization, Project, "dup-label", 365);

        cert1.Label.ShouldBe("dup-label");
        cert2.Label.ShouldStartWith("dup-label-");
        cert2.Label.Length.ShouldBeGreaterThan("dup-label".Length);
    }

    private async Task EnsureAccessPointAsync()
    {
        try
        {
            var accessService = fixture.Services.GetRequiredService<IAccessService>();
            await accessService.CreateAccessPointAsync(new AccessPointCreate
            {
                Organization = Organization,
                Project = Project,
                OwnerId = "test-owner",
            });
        }
        catch
        {
            // Already exists.
        }
    }
}
