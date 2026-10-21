using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// The scopes of a machine through CosmosAccessService and real storage: the machine document, the API key read back
/// from it, and the certificate record.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ScopesPersistenceTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "scopesproj";

    // Runtime, secrets and management scopes together, in enum order, as the API stores them.
    private static readonly MachineScope[] Granted =
    [
        MachineScope.AnnotationRead,
        MachineScope.ConfigurationRead,
        MachineScope.ConfigurationWrite,
        MachineScope.SecretsRead,
        MachineScope.ConfigurationTransfer,
        MachineScope.SchemaWrite,
    ];

    [Fact]
    public async Task ApiKeyMachine_KeepsItsScopes_WhenStoredValidatedAndReset()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.ApiKey);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey, Granted));

        machine.Scopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
        var validated = await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey);
        validated.ShouldNotBeNull();
        validated.Scopes.ShouldBe(Granted);

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id);

        reset.Scopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
        (await fixture.Services.GetRequiredService<IApiKeyHashStore>().GetAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task CertificateMachine_KeepsItsScopes_InTheMachineAndTheCertificateRecord()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.Certificate);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.Certificate, Granted));

        machine.Scopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
        var record = await fixture.Services.GetRequiredService<IClientCertificateStore>().GetByMachineAccessIdAsync(Organization, Project, machine.Id);
        record.ShouldNotBeNull();
        record.Scopes.ShouldBe(Granted);

        var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(machine.Certificate!), machine.CertificatePassword);
        var validated = await fixture.Services.GetRequiredService<IClientCertificateValidator>().ValidateAsync(certificate);

        validated.ShouldNotBeNull();
        validated.MachineAccessId.ShouldBe(machine.Id);
        validated.Scopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task CertificateAndApiKeyMachine_CarriesItsScopesInBothCredentials_AndKeepsThemOnReset()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.CertificateAndApiKey);

        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey, Granted));

        machine.Scopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
        (await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey))!.Scopes.ShouldBe(Granted);
        var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(machine.Certificate!), machine.CertificatePassword);
        (await fixture.Services.GetRequiredService<IClientCertificateValidator>().ValidateAsync(certificate))!.Scopes.ShouldBe(Granted);

        // A reset replaces the API key only; both credentials keep the stored list.
        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id);

        reset.Scopes.ShouldBe(Granted);
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
        (await fixture.Services.GetRequiredService<IApiKeyHashStore>().GetAsync(Organization, Project, machine.Id))!.Scopes.ShouldBe(Granted);
        var record = await fixture.Services.GetRequiredService<IClientCertificateStore>().GetByMachineAccessIdAsync(Organization, Project, machine.Id);
        record!.Scopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task MachineWithoutScopes_StoresAnEmptyList()
    {
        var accessService = await PrepareAsync(MachineCredentialKind.CertificateAndApiKey);

        // The API refuses a machine without a runtime scope; below it, a missing list is stored as none, never null.
        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey, null));

        machine.Scopes.ShouldBeEmpty();
        (await accessService.GetMachineAccessAsync(Organization, Project, machine.Id))!.Scopes.ShouldBeEmpty();
        (await fixture.Services.GetRequiredService<IApiKeyValidator>().ValidateAsync(machine.AccessKey))!.Scopes.ShouldBeEmpty();
        var record = await fixture.Services.GetRequiredService<IClientCertificateStore>().GetByMachineAccessIdAsync(Organization, Project, machine.Id);
        record!.Scopes.ShouldBeEmpty();
    }

    private static MachineAccessCreate Model(MachineCredentialKind kind, IReadOnlyList<MachineScope>? scopes)
    {
        return new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scopes = scopes,
            CredentialKind = kind,
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
