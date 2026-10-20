using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// The management scopes of a machine through CosmosAccessService and real storage: the machine document, the API key
/// read back from it, and the certificate record.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ManagementScopesPersistenceTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "manageproj";

    private static readonly MachineManagementScope[] Granted = [MachineManagementScope.ConfigurationTransfer, MachineManagementScope.SchemaWrite];

    [Fact]
    public async Task ApiKeyMachine_KeepsItsManagementScopes_WhenStoredValidatedAndReset()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.ApiKey);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey, Granted));

        machine.ManagementScopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.ManagementScopes.ShouldBe(Granted);
        var validated = await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey);
        validated.ShouldNotBeNull();
        validated.ManagementScopes.ShouldBe(Granted);

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id);

        reset.ManagementScopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.ManagementScopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task CertificateMachine_KeepsItsManagementScopes_InTheMachineAndTheCertificateRecord()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.Certificate);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.Certificate, Granted));

        machine.ManagementScopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.ManagementScopes.ShouldBe(Granted);
        var record = await fixture.Services.GetRequiredService<IClientCertificateStore>().GetByMachineAccessIdAsync(Organization, Project, machine.Id);
        record.ShouldNotBeNull();
        record.ManagementScopes.ShouldBe(Granted);

        var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(machine.Certificate!), machine.CertificatePassword);
        var validated = await fixture.Services.GetRequiredService<IClientCertificateValidator>().ValidateAsync(certificate);

        validated.ShouldNotBeNull();
        validated.MachineAccessId.ShouldBe(machine.Id);
        validated.ManagementScopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task CertificateAndApiKeyMachine_CarriesItsManagementScopesInBothCredentials()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.CertificateAndApiKey);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey, Granted));

        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.ManagementScopes.ShouldBe(Granted);
        (await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey))!.ManagementScopes.ShouldBe(Granted);
        var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(machine.Certificate!), machine.CertificatePassword);
        (await fixture.Services.GetRequiredService<IClientCertificateValidator>().ValidateAsync(certificate))!.ManagementScopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task MachineWithoutManagementScopes_StoresNone()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.CertificateAndApiKey);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey, null));

        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.ManagementScopes.ShouldBeNull();
        (await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey))!.ManagementScopes.ShouldBeNull();
        var record = await fixture.Services.GetRequiredService<IClientCertificateStore>().GetByMachineAccessIdAsync(Organization, Project, machine.Id);
        record!.ManagementScopes.ShouldBeNull();
    }

    private static MachineAccessCreate Model(MachineCredentialKind kind, IReadOnlyList<MachineManagementScope>? managementScopes)
    {
        return new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.DefaultReadWrite,
            CredentialKind = kind,
            ManagementScopes = managementScopes,
        };
    }

    private async Task<IAccessService> PrepareAsync(MachineCredentialKind kind)
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();

        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = Project, OwnerId = "test-owner" });
        }
        catch (ConflictException)
        {
            // Already exists.
        }

        await fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>().SetAsync(Organization, Project, new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate, MachineCredentialKind.CertificateAndApiKey],
            CredentialKind = kind,
        });

        return accessService;
    }
}
