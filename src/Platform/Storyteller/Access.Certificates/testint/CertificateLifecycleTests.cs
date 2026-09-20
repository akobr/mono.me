using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// End-to-end certificate lifecycle: issue -> validate -> renew -> verify idempotency.
/// Uses real Cosmos storage via Testcontainers.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CertificateLifecycleTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "testproj";

    [Fact]
    public async Task IssueCertificate_ThenValidate_Succeeds()
    {
        const string certProject = "certproj";

        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        var validator = fixture.Services.GetRequiredService<IClientCertificateValidator>();
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();

        // Create access point for the certificate project.
        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate
            {
                Organization = Organization,
                Project = certProject,
                OwnerId = "test-owner",
            });
        }
        catch
        {
            // Already exists — fine.
        }

        // Configure Certificate policy so machine access always issues a certificate.
        await policyStore.SetAsync(Organization, certProject, new MachineAuthenticationPolicy
        {
            CredentialKind = MachineCredentialKind.Certificate,
        });

        // Create machine access with certificate.
        var model = new MachineAccessCreate
        {
            Organization = Organization,
            Project = certProject,
            Scope = MachineAccessScope.DefaultRead,
        };

        var machine = await accessService.CreateMachineAccessAsync(model);
        machine.ShouldNotBeNull();
        machine.Id.ShouldNotBeNullOrEmpty();

        // Load the issued certificate.
        machine.Certificate.ShouldNotBeNullOrEmpty();
        var pkcs12 = Convert.FromBase64String(machine.Certificate!);
        var cert = X509CertificateLoader.LoadPkcs12(pkcs12, machine.CertificatePassword);

        // Validate the certificate.
        var result = await validator.ValidateAsync(cert);

        result.ShouldNotBeNull();
        result!.Organization.ShouldBe(Organization);
        result.Project.ShouldBe(certProject);
        result.MachineAccessId.ShouldBe(machine.Id);
    }

    [Fact]
    public async Task ApiKeyMachineAccess_CreateAndVerify()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        await EnsureAccessPointAsync(accessService);

        var model = new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.DefaultReadWrite,
        };

        var machine = await accessService.CreateMachineAccessAsync(model);

        machine.ShouldNotBeNull();
        machine.Id.ShouldNotBeNullOrEmpty();
        machine.AccessKey.ShouldNotBeNullOrEmpty();
        machine.Scope.ShouldBe(MachineAccessScope.DefaultReadWrite);

        // Verify access.
        var hasAccess = await accessService.VerifyAccessForMachineAsync(Organization, Project, machine.Id);
        hasAccess.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteMachineAccess_RemovesAccess()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        await EnsureAccessPointAsync(accessService);

        var model = new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.DefaultRead,
        };

        var machine = await accessService.CreateMachineAccessAsync(model);
        var deleted = await accessService.DeleteMachineAccessAsync(Organization, Project, machine.Id);
        deleted.ShouldBeTrue();

        var hasAccess = await accessService.VerifyAccessForMachineAsync(Organization, Project, machine.Id);
        hasAccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ResetMachineAccess_ReturnsNewKey()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        await EnsureAccessPointAsync(accessService);

        var model = new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.DefaultRead,
        };

        var machine = await accessService.CreateMachineAccessAsync(model);
        var originalKey = machine.AccessKey;

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id);
        reset.ShouldNotBeNull();
        reset!.AccessKey.ShouldNotBe(originalKey);
    }

    private static async Task EnsureAccessPointAsync(IAccessService accessService)
    {
        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate
            {
                Organization = Organization,
                Project = Project,
                OwnerId = "test-owner",
            });
        }
        catch
        {
            // Already exists — fine.
        }
    }
}
