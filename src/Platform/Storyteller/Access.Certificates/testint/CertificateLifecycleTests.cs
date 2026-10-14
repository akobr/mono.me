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
    public async Task MachinesOfTheEnabledKinds_KeepTheirKindLifetimeAndExpiry()
    {
        const string mixedProject = "mixedproj";

        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync(accessService, mixedProject);
        await policyStore.SetAsync(Organization, mixedProject, new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate],
            CredentialKind = MachineCredentialKind.ApiKey,
            CertificateLifetimeDays = 120,
        });

        var apiKeyMachine = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = mixedProject,
            Scope = MachineAccessScope.DefaultRead,
        });
        var certificateMachine = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = mixedProject,
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.Certificate,
        });

        var storedApiKey = await accessService.GetMachineAccessAsync(Organization, mixedProject, apiKeyMachine.Id);
        var storedCertificate = await accessService.GetMachineAccessAsync(Organization, mixedProject, certificateMachine.Id);

        storedApiKey!.CredentialKind.ShouldBe(MachineCredentialKind.ApiKey);
        storedApiKey.CertificateLifetimeDays.ShouldBeNull();
        storedCertificate!.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        storedCertificate.CertificateLifetimeDays.ShouldBe(120);
        storedCertificate.CertificateNotAfter.ShouldNotBeNull();
        storedCertificate.CertificateNotAfter.Value.ShouldBe(DateTimeOffset.UtcNow.AddDays(120), TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task SharedCertificateMachine_IsStoredWithoutACertificateOfItsOwn()
    {
        const string sharedProject = "sharedmachines";

        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync(accessService, sharedProject);
        await policyStore.SetAsync(Organization, sharedProject, new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.CertificateAndApiKey],
        });

        var machine = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = sharedProject,
            Scope = MachineAccessScope.DefaultRead,
            UseSharedCertificate = true,
        });

        machine.Certificate.ShouldBeNull();
        machine.AccessKey.ShouldStartWith("2s.");

        var stored = await accessService.GetMachineAccessAsync(Organization, sharedProject, machine.Id);
        stored!.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        stored.UsesSharedCertificate.ShouldBeTrue();
        stored.CertificateThumbprint.ShouldBeNull();
        stored.CertificateLifetimeDays.ShouldBeNull();
        stored.CertificateNotAfter.ShouldBeNull();

        var validated = await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey);
        validated!.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
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

    private static async Task EnsureAccessPointAsync(IAccessService accessService, string project = Project)
    {
        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate
            {
                Organization = Organization,
                Project = project,
                OwnerId = "test-owner",
            });
        }
        catch
        {
            // Already exists — fine.
        }
    }
}
