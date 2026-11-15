using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// The access audit records of a machine's creation and deletion, of every kind, through the access service, the
/// policy-aware router and real storage: one record each, in the machines' partition, with what decides the access and
/// never a credential. A reset that is refused records nothing.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MachineAuditTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "auditmachines";
    private const string AnnotationKey = "sbt.north";
    private const string PartitionKey = $"{Project}.access";

    private static readonly AccessCaller Admin = AccessCaller.Account("admin-1", "Ada Admin") with { CorrelationId = "invocation-machines" };

    private static readonly MachineScope[] Granted = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead];

    private static readonly FederationRule Rule = new() { Issuer = "github", Audience = "urn:storyteller:test.platform:api", Subject = "repo:acme/app:*" };

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, false)]
    [InlineData(MachineCredentialKind.Certificate, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true)]
    [InlineData(MachineCredentialKind.ClientCredentials, false)]
    [InlineData(MachineCredentialKind.Federated, false)]
    public async Task CreateAndDelete_RecordOneChangeEach_WithoutACredential(MachineCredentialKind kind, bool useSharedCertificate)
    {
        var accessService = await PrepareAsync();

        var machine = await accessService.CreateMachineAccessAsync(Model(kind) with { UseSharedCertificate = useSharedCertificate }, Admin);

        var hash = (await fixture.Services.GetRequiredService<IApiKeyHashStore>().GetAsync(Organization, Project, machine.Id))?.HashedSecret;
        var created = (await RecordsAsync(machine.Id)).ShouldHaveSingleItem();
        created.Record.Action.ShouldBe(AccessAuditAction.MachineCreated);
        created.ShouldBeBy(Admin, Organization, Project, PartitionKey);
        created.Record.Target.ShouldBe(AccessAuditTarget.Machine(machine.Id));
        created.Record.OldValue.ShouldBeNull();
        ShouldDescribe(created.Record.NewValue, machine, kind, useSharedCertificate);
        created.ShouldHoldNoSecret(machine.AccessKey, hash, machine.Certificate, machine.CertificatePassword);

        if (kind == MachineCredentialKind.Federated)
        {
            created.Record.Detail.ShouldNotBeNull().ShouldStartWith("Federation rules: ");
            created.Record.Detail.ShouldContain("repo:acme/app:*");
        }
        else
        {
            created.Record.Detail.ShouldBeNull();
        }

        (await accessService.DeleteMachineAccessAsync(Organization, Project, machine.Id, Admin)).ShouldBeTrue();

        // A machine already gone changes nothing, so it records nothing.
        (await accessService.DeleteMachineAccessAsync(Organization, Project, machine.Id, Admin)).ShouldBeFalse();

        var records = await RecordsAsync(machine.Id);
        records.Select(stored => stored.Record.Action).ShouldBe([AccessAuditAction.MachineCreated, AccessAuditAction.MachineDeleted]);
        var deleted = records[1];
        deleted.ShouldBeBy(Admin, Organization, Project, PartitionKey);
        deleted.Record.Target.ShouldBe(AccessAuditTarget.Machine(machine.Id));
        deleted.Record.OldValue.ShouldBe(created.Record.NewValue);
        deleted.Record.NewValue.ShouldBeNull();
        deleted.ShouldHoldNoSecret(machine.AccessKey, hash, machine.Certificate, machine.CertificatePassword);
    }

    // Neither kind has a secret to reset; the refusal changes nothing, so it records nothing.
    [Theory]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.Federated)]
    public async Task Reset_OfAMachineWithoutASecretToReset_IsRefused_AndRecordsNothing(MachineCredentialKind kind)
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(kind), AccessCaller.System);

        await Should.ThrowAsync<MachineAccessNotSupportedException>(() => accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, Admin));

        (await RecordsAsync(machine.Id)).Select(stored => stored.Record.Action).ShouldBe([AccessAuditAction.MachineCreated]);
    }

    // The machine's value: its kind, scopes and annotation key, the thumbprint of its own certificate, and the shared
    // certificate only when it uses one.
    private static void ShouldDescribe(string? value, MachineAccess machine, MachineCredentialKind kind, bool useSharedCertificate)
    {
        var hasOwnCertificate = kind == MachineCredentialKind.Certificate || (kind == MachineCredentialKind.CertificateAndApiKey && !useSharedCertificate);
        (machine.CertificateThumbprint is not null).ShouldBe(hasOwnCertificate);

        var json = StoredAccessAudit.Json(value);
        json["CredentialKind"]!.GetValue<string>().ShouldBe(kind.ToString());
        json["Scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()).ShouldBe(Granted.Select(scope => scope.ToString()));
        json["AnnotationKey"]!.GetValue<string>().ShouldBe(AnnotationKey);
        (json["UsesSharedCertificate"]?.GetValue<bool>()).ShouldBe(useSharedCertificate ? true : null);
        (json["CertificateThumbprint"]?.GetValue<string>()).ShouldBe(machine.CertificateThumbprint);
    }

    private static MachineAccessCreate Model(MachineCredentialKind kind)
    {
        return new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scopes = Granted,
            AnnotationKey = AnnotationKey,
            CredentialKind = kind,
            FederationRules = kind == MachineCredentialKind.Federated ? [Rule] : null,
        };
    }

    // The machine's records, oldest first.
    private Task<IReadOnlyList<StoredAccessAudit>> RecordsAsync(string machineId)
    {
        return StoredAccessAudit.ReadMachineAsync(fixture.Services, Organization, Project, machineId);
    }

    // A project that enables every kind, with the trusted issuer of the federated machines.
    private async Task<IAccessService> PrepareAsync()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();

        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = Project, OwnerId = "test-owner" }, AccessCaller.System);
        }
        catch (ConflictException)
        {
            // Already exists.
        }

        await fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>().SetAsync(
            Organization,
            Project,
            new MachineAuthenticationPolicy
            {
                EnabledCredentialKinds =
                [
                    MachineCredentialKind.ApiKey,
                    MachineCredentialKind.Certificate,
                    MachineCredentialKind.CertificateAndApiKey,
                    MachineCredentialKind.ClientCredentials,
                    MachineCredentialKind.Federated,
                ],
                CredentialKind = MachineCredentialKind.ApiKey,
            },
            AccessCaller.System);
        await fixture.Services.GetRequiredService<IFederatedIssuerStore>().SetAsync(
            Organization,
            Project,
            new FederatedIssuer
            {
                Name = "github",
                Issuer = "https://token.actions.githubusercontent.com",
                RequiredClaims = new Dictionary<string, IReadOnlyList<string>> { ["repository_owner_id"] = ["1342004"] },
            },
            AccessCaller.System);

        return accessService;
    }
}
