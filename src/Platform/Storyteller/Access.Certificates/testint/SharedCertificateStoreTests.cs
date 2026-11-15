using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// Tests shared certificate store operations against real Cosmos.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SharedCertificateStoreTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "sharedproj";

    private static readonly AccessCaller Admin = AccessCaller.Account("admin-1", "Ada Admin") with { CorrelationId = "invocation-certificates" };

    [Fact]
    public async Task IssueAndList_SharedCertificate()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var cert = await service.IssueAsync(Organization, Project, "my-service", 365, AccessCaller.System);

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

        var cert = await service.IssueAsync(Organization, Project, "to-revoke", 365, AccessCaller.System);
        var revoked = await service.RevokeAsync(Organization, Project, cert.Thumbprint, AccessCaller.System);

        revoked.ShouldBeTrue();

        var list = await service.ListAsync(Organization, Project);
        var revokedCert = list.FirstOrDefault(c => c.Thumbprint == cert.Thumbprint);
        revokedCert.ShouldNotBeNull();
        revokedCert!.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoke_NonExistent_ReturnsFalse()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var result = await service.RevokeAsync(Organization, Project, "NONEXISTENT_THUMBPRINT", AccessCaller.System);
        result.ShouldBeFalse();
        (await RecordsAsync(Project, "NONEXISTENT_THUMBPRINT")).ShouldBeEmpty();
    }

    // The issue and the revocation each record the certificate with its label and validity, in the batch of the
    // change, never the PFX or its password; revoking a revoked certificate changes and records nothing.
    [Fact]
    public async Task IssueAndRevoke_RecordOneChangeEach_WithoutThePfxOrItsPassword()
    {
        const string project = "sharedaudit";
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync(project);

        var certificate = await service.IssueAsync(Organization, project, "audited", 365, Admin);

        var issued = (await RecordsAsync(project, certificate.Thumbprint)).ShouldHaveSingleItem();
        issued.Record.Action.ShouldBe(AccessAuditAction.SharedCertificateIssued);
        issued.ShouldBeBy(Admin, Organization, project, $"{project}.access");
        issued.Record.Target.ShouldBe(AccessAuditTarget.Certificate(certificate.Thumbprint, "audited"));
        issued.Record.OldValue.ShouldBeNull();
        var value = StoredAccessAudit.Json(issued.Record.NewValue);
        value["Label"]!.GetValue<string>().ShouldBe("audited");
        value["NotBefore"]!.GetValue<DateTimeOffset>().ShouldBe(certificate.NotBefore);
        value["NotAfter"]!.GetValue<DateTimeOffset>().ShouldBe(certificate.NotAfter);
        value.ContainsKey("IsRevoked").ShouldBeFalse();
        issued.ShouldHoldNoSecret(certificate.Certificate, certificate.CertificatePassword);

        (await service.RevokeAsync(Organization, project, certificate.Thumbprint, Admin)).ShouldBeTrue();
        (await service.RevokeAsync(Organization, project, certificate.Thumbprint, AccessCaller.System)).ShouldBeTrue();

        var records = await RecordsAsync(project, certificate.Thumbprint);
        records.Select(stored => stored.Record.Action).ShouldBe([AccessAuditAction.SharedCertificateIssued, AccessAuditAction.SharedCertificateRevoked]);
        var revoked = records[1];
        revoked.ShouldBeBy(Admin, Organization, project, $"{project}.access");
        revoked.Record.Target.ShouldBe(AccessAuditTarget.Certificate(certificate.Thumbprint, "audited"));
        revoked.Record.OldValue.ShouldBe(issued.Record.NewValue);
        StoredAccessAudit.Json(revoked.Record.NewValue)["IsRevoked"]!.GetValue<bool>().ShouldBeTrue();
        revoked.ShouldHoldNoSecret(certificate.Certificate, certificate.CertificatePassword);

        // The machine list of the partition skips the records, as it skips the certificates.
        (await fixture.Services.GetRequiredService<IAccessService>().GetMachineAccessesAsync(Organization, project)).ShouldBeEmpty();
    }

    // Two revocations at once both read the certificate unrevoked; only one of them revokes it, so only one may be
    // recorded, as a revocation of a revoked certificate records nothing.
    [Fact]
    public async Task Revoke_RacingAnotherRevoke_RecordsOneRevocation()
    {
        const string project = "sharedrace";
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync(project);
        var certificate = await service.IssueAsync(Organization, project, "raced", 365, AccessCaller.System);
        var other = fixture.Services.GetRequiredService<ISharedCertificateStore>();
        var reads = 0;
        var store = new CosmosSharedCertificateStore(
            CreateInterleavingProvider(async () =>
            {
                // The other revocation lands between this one's read and its write.
                if (++reads == 1)
                {
                    (await other.RevokeAsync(Organization, project, certificate.Thumbprint, AccessCaller.System)).ShouldBeTrue();
                }
            }),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));

        (await store.RevokeAsync(Organization, project, certificate.Thumbprint, Admin)).ShouldBeTrue();

        (await RecordsAsync(project, certificate.Thumbprint))
            .Count(stored => stored.Record.Action == AccessAuditAction.SharedCertificateRevoked)
            .ShouldBe(1);
    }

    [Fact]
    public async Task DuplicateLabel_AppendsSuffix()
    {
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync();

        var cert1 = await service.IssueAsync(Organization, Project, "dup-label", 365, AccessCaller.System);
        var cert2 = await service.IssueAsync(Organization, Project, "dup-label", 365, AccessCaller.System);

        cert1.Label.ShouldBe("dup-label");
        cert2.Label.ShouldStartWith("dup-label-");
        cert2.Label.Length.ShouldBeGreaterThan("dup-label".Length);
    }

    [Fact]
    public async Task Issue_InAProjectWithoutCertificateAndApiKey_IsNotSupported()
    {
        const string apiKeyProject = "sharedoff";
        var service = fixture.Services.GetRequiredService<SharedCertificateService>();
        await EnsureAccessPointAsync(apiKeyProject, MachineCredentialKind.ApiKey);

        await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => service.IssueAsync(Organization, apiKeyProject, "not-allowed", 365, AccessCaller.System));
        (await service.ListAsync(Organization, apiKeyProject)).ShouldBeEmpty();
    }

    // The access audit records of a shared certificate, oldest first.
    // The organization's real container behind a wrapper that runs the step after each read, as a concurrent writer
    // would land between a read and the write after it; the batches go through.
    private IContainerRepositoryProvider CreateInterleavingProvider(Func<Task> afterRead)
    {
        var real = fixture.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Organization).Container;
        var container = new Mock<Container>(MockBehavior.Strict);
        container
            .Setup(c => c.ReadItemStreamAsync(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string id, PartitionKey partitionKey, ItemRequestOptions options, CancellationToken cancellation) =>
            {
                var response = await real.ReadItemStreamAsync(id, partitionKey, options, cancellation);
                await afterRead();
                return response;
            });
        container
            .Setup(c => c.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns((PartitionKey partitionKey) => real.CreateTransactionalBatch(partitionKey));

        // a real scope over the wrapper (the class is sealed): the keys are the organization's, the calls reach the wrapper
        var repository = new OrganizationContainer(Organization, container.Object);
        return Mock.Of<IContainerRepositoryProvider>(p => p.GetOrganizationContainer(Organization) == repository);
    }

    private async Task<IReadOnlyList<StoredAccessAudit>> RecordsAsync(string project, string thumbprint)
    {
        return (await StoredAccessAudit.ReadAsync(fixture.Services, Organization, $"{project}.access"))
            .Where(stored => stored.Record.Target?.Kind == AccessAuditTargetKind.Certificate && stored.Record.Target.Id == thumbprint)
            .ToList();
    }

    // Shared certificates follow CertificateAndApiKey, so the project enables it.
    private async Task EnsureAccessPointAsync(string project = Project, MachineCredentialKind kind = MachineCredentialKind.CertificateAndApiKey)
    {
        try
        {
            var accessService = fixture.Services.GetRequiredService<IAccessService>();
            await accessService.CreateAccessPointAsync(
                new AccessPointCreate
                {
                    Organization = Organization,
                    Project = project,
                    OwnerId = "test-owner",
                },
                AccessCaller.System);
        }
        catch
        {
            // Already exists.
        }

        await fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>().SetAsync(
            Organization,
            project,
            new MachineAuthenticationPolicy { EnabledCredentialKinds = [kind] },
            AccessCaller.System);
    }
}
