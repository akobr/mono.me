using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// Tests certificate renewal against real Cosmos, including ETag CAS idempotency.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CertificateRenewalTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "renewproj";

    [Fact]
    public async Task Renew_WithCurrentThumbprint_InWindow_Succeeds()
    {
        var renewalService = fixture.Services.GetRequiredService<ICertificateRenewalService>();
        var (machineAccessId, thumbprint) = await CreateMachineWithCertificateNearExpiry();

        var result = await renewalService.RenewAsync(Organization, Project, machineAccessId, thumbprint);

        result.Outcome.ShouldBe(CertificateRenewalOutcome.Success);
        result.Pkcs12.ShouldNotBeNull();
        result.Pkcs12.ShouldNotBeEmpty();
        result.Password.ShouldNotBeNullOrEmpty();
        result.Thumbprint.ShouldNotBeNullOrEmpty();
        result.Thumbprint.ShouldNotBe(thumbprint); // new thumbprint
    }

    [Fact]
    public async Task Renew_WithPreviousThumbprint_ReturnsAlreadyRenewed()
    {
        var renewalService = fixture.Services.GetRequiredService<ICertificateRenewalService>();
        var (machineAccessId, oldThumbprint) = await CreateMachineWithCertificateNearExpiry();

        // First renewal — should succeed.
        var firstResult = await renewalService.RenewAsync(Organization, Project, machineAccessId, oldThumbprint);
        firstResult.Outcome.ShouldBe(CertificateRenewalOutcome.Success);

        // Second renewal with old thumbprint — should be "already renewed".
        var secondResult = await renewalService.RenewAsync(Organization, Project, machineAccessId, oldThumbprint);
        secondResult.Outcome.ShouldBe(CertificateRenewalOutcome.AlreadyRenewed);
        secondResult.Message.ShouldContain("already renewed");
        secondResult.Pkcs12.ShouldBeNull();
    }

    [Fact]
    public async Task Renew_WithNewThumbprint_NotInWindow()
    {
        var renewalService = fixture.Services.GetRequiredService<ICertificateRenewalService>();
        var (machineAccessId, oldThumbprint) = await CreateMachineWithCertificateNearExpiry();

        // Renew once.
        var renewResult = await renewalService.RenewAsync(Organization, Project, machineAccessId, oldThumbprint);
        renewResult.Outcome.ShouldBe(CertificateRenewalOutcome.Success);

        // Try renewal with the new thumbprint — window is ~335 days away.
        var newThumbprint = renewResult.Thumbprint!;
        var thirdResult = await renewalService.RenewAsync(Organization, Project, machineAccessId, newThumbprint);
        thirdResult.Outcome.ShouldBe(CertificateRenewalOutcome.NotInRenewalWindow);
    }

    [Fact]
    public async Task Renew_NonExistentMachine_ReturnsNotFound()
    {
        var renewalService = fixture.Services.GetRequiredService<ICertificateRenewalService>();

        var result = await renewalService.RenewAsync(Organization, Project, "nonexistent-id", "FAKE_THUMBPRINT");

        result.Outcome.ShouldBe(CertificateRenewalOutcome.NotFound);
    }

    [Fact]
    public async Task Renew_ThumbprintMismatch_ReturnsNotFound()
    {
        var renewalService = fixture.Services.GetRequiredService<ICertificateRenewalService>();
        var (machineAccessId, _) = await CreateMachineWithCertificateNearExpiry();

        var result = await renewalService.RenewAsync(Organization, Project, machineAccessId, "WRONG_THUMBPRINT");

        result.Outcome.ShouldBe(CertificateRenewalOutcome.NotFound);
    }

    [Fact]
    public async Task Renew_OutsideWindow_ReturnsNotInWindow()
    {
        var renewalService = fixture.Services.GetRequiredService<ICertificateRenewalService>();
        var (machineAccessId, thumbprint) = await CreateMachineWithCertificateFarFromExpiry();

        var result = await renewalService.RenewAsync(Organization, Project, machineAccessId, thumbprint);

        result.Outcome.ShouldBe(CertificateRenewalOutcome.NotInRenewalWindow);
        result.Message.ShouldContain("renewal window");
    }

    /// <summary>
    /// Creates a machine access entity with a certificate that is within the renewal window
    /// (NotAfter in 15 days, within the 30-day RenewalWindowDays).
    /// </summary>
    private async Task<(string MachineAccessId, string Thumbprint)> CreateMachineWithCertificateNearExpiry()
    {
        await EnsureAccessPointAsync();

        var authority = fixture.Services.GetRequiredService<IClientCertificateAuthority>();
        var opts = fixture.Services.GetRequiredService<IOptions<MachineAuthenticationOptions>>().Value;

        var machineId = Guid.NewGuid().ToString("D");
        var issued = await authority.IssueCertificateAsync(Organization, Project, machineId, MachineAccessScope.DefaultRead, 30);

        // Write the entity directly into Cosmos with NotAfter set to 15 days from now.
        var repositoryProvider = fixture.Services.GetRequiredService<IContainerRepositoryProvider>();
        var repository = repositoryProvider.GetOrganizationContainer(Organization);
        var partitionKeyValue = $"{Project}.access";
        var partitionKey = new PartitionKey(partitionKeyValue);

        var entity = new MachineAccessEntity
        {
            PartitionKey = partitionKeyValue,
            Id = machineId,
            ObjectId = machineId,
            AccessKey = "***",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.Certificate,
            CertificateThumbprint = issued.Thumbprint,
            CertificateSerialNumber = issued.SerialNumber,
            NotBefore = DateTimeOffset.UtcNow.AddDays(-15),
            NotAfter = DateTimeOffset.UtcNow.AddDays(15), // within 30-day renewal window
        };

        await repository.Container.UpsertItemAsync(entity, partitionKey);

        // Also store a certificate record so the validator can find it.
        var certStore = fixture.Services.GetRequiredService<IClientCertificateStore>();
        await certStore.StoreAsync(Organization, Project, new CertificateRecord(
            machineId, issued.Thumbprint, null, null, issued.SerialNumber,
            entity.NotBefore.Value, entity.NotAfter.Value, false,
            MachineAccessScope.DefaultRead, null, null));

        return (machineId, issued.Thumbprint);
    }

    /// <summary>
    /// Creates a machine access entity with a certificate far from expiry (335 days out).
    /// </summary>
    private async Task<(string MachineAccessId, string Thumbprint)> CreateMachineWithCertificateFarFromExpiry()
    {
        await EnsureAccessPointAsync();

        var authority = fixture.Services.GetRequiredService<IClientCertificateAuthority>();

        var machineId = Guid.NewGuid().ToString("D");
        var issued = await authority.IssueCertificateAsync(Organization, Project, machineId, MachineAccessScope.DefaultRead, 365);

        var repositoryProvider = fixture.Services.GetRequiredService<IContainerRepositoryProvider>();
        var repository = repositoryProvider.GetOrganizationContainer(Organization);
        var partitionKeyValue = $"{Project}.access";
        var partitionKey = new PartitionKey(partitionKeyValue);

        var entity = new MachineAccessEntity
        {
            PartitionKey = partitionKeyValue,
            Id = machineId,
            ObjectId = machineId,
            AccessKey = "***",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.Certificate,
            CertificateThumbprint = issued.Thumbprint,
            CertificateSerialNumber = issued.SerialNumber,
            NotBefore = issued.NotBefore,
            NotAfter = issued.NotAfter, // ~365 days away — outside 30-day window
        };

        await repository.Container.UpsertItemAsync(entity, partitionKey);

        return (machineId, issued.Thumbprint);
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
