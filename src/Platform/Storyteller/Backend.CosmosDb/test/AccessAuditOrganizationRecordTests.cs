#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The access audit records of the container organizations: machines and shared certificates in (organization,
// "{project}.access"), trusted issuers in (organization, "{project}.federation"). A change writes exactly one record in
// the batch of its change, never a secret; a change that changes nothing writes none, a failed batch stores neither, and
// the lists of machines and issuers never show a record.
public class AccessAuditOrganizationRecordTests(Startup startup)
    : BaseTestsClass(startup)
{
    // A JWKS with two RSA keys: the records count them and never copy one.
    private const string Modulus = "sXchDaQebHnPiGvyDOAT4saGEUetSyo9MKLOoWFsueri23bOdgWp4Dy1WlUzewbgBHod5pcM9H95GQRV3JDXboIRROSBigeC5yjU1hGzHHyXss8UDprecbAYxknTcQkhslANGRUZmdTOQ5qTRsLAt6BTYuyvVRdhS8exSZEy_c4gs_7svlJJQ4H9_NxsiIoLwAEk7-Q3UXERGYw_75IDrGA84-lA_-Ct4eTlXHBIY2EaV7t7LjJaynVJCpkv4LKjTTAumiGUIuQhrNhZLuF_RJLqHpM2kgWFLU7-VTdL1VbC2tejvcI2BlMkEpk1BzBZI0KQB0GaDWFLN-aEAw3vRw";

    private const string TwoKeys = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"one\",\"n\":\"" + Modulus + "\",\"e\":\"AQAB\"},{\"kty\":\"RSA\",\"kid\":\"two\",\"n\":\"" + Modulus + "\",\"e\":\"AQAB\"}]}";

    // One organization per run of the class, so a reused emulator never mixes runs; every test has a project of its own.
    private static readonly string Organization = $"aud-org-{Guid.NewGuid().ToString("N")[..8]}";

    private static readonly MachineScope[] Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead];

    private static readonly AccessCaller ViaMcp = new(AuditActor.Account("aud-admin", "Ada") with { Via = "mcp" }) { CorrelationId = "corr-1" };

    private static readonly AccessCaller Admin = AccessCaller.Account("aud-admin", "Ada");

    private IContainerRepositoryProvider Repositories => Context.Services.GetRequiredService<IContainerRepositoryProvider>();

    private IOptions<JsonSerializerOptions> SerializerOptions => Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>();

    [Fact]
    public async Task CreateMachine_RecordsItsKindAndScopes_NeverItsKeyOrHash()
    {
        var project = NewName("p");

        var machine = await Machines(Repositories).CreateMachineAccessAsync(ApiKeyMachine(project), ViaMcp);

        var record = (await ReadAsync(AccessPartition(project))).Should().ContainSingle().Subject;
        record.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineCreated, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Machine(machine.Id),
            NewValue = "{\"CredentialKind\":\"ApiKey\",\"Scopes\":[\"AnnotationRead\",\"ConfigurationRead\"],\"AnnotationKey\":\"unit.billing\"}",
        });
        var stored = await ReadRawAsync(AccessPartition(project), record.Id);
        stored.Should().NotContain(machine.AccessKey).And.NotContain(MachineProviderDouble.FirstHash).And.NotContain("***");
    }

    [Fact]
    public async Task ResetMachine_ApiKey_RecordsOnceInTheBatchOfTheNewHash()
    {
        var project = NewName("p");
        var storage = new InterceptedRepositoryProvider(Repositories);
        var machines = Machines(storage);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project), Admin);

        var added = await AddedAsync(AccessPartition(project), async () =>
            (await machines.ResetMachineAccessAsync(Organization, project, machine.Id, ViaMcp)).AccessKey.Should().Be(MachineProviderDouble.SecondKey));

        var record = added.Should().ContainSingle().Subject;
        record.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineReset, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Machine(machine.Id),
            NewValue = "ApiKey",
            Detail = "The API key was replaced.",
        });

        // the patch of the hash is the reset, guarded by the ETag of the machine, and its record shares its batch
        var batch = storage.Interceptor.Batches.Should().ContainSingle(candidate => candidate.Contains(BatchOperationKind.Create, record.Id)).Subject;
        batch.Operations.Select(operation => (operation.Kind, operation.Id)).Should().Equal((BatchOperationKind.Patch, machine.Id), (BatchOperationKind.Create, record.Id));
        batch.Operations[0].IfMatchEtag.Should().NotBeNullOrEmpty();
        (await HashStore(Repositories).GetAsync(Organization, project, machine.Id))!.HashedSecret.Should().Be(MachineProviderDouble.SecondHash);
        (await ReadRawAsync(AccessPartition(project), record.Id)).Should().NotContain(MachineProviderDouble.SecondKey).And.NotContain(MachineProviderDouble.SecondHash);
    }

    [Fact]
    public async Task ResetMachine_ClientCredentials_RecordsInTheBatchOfTheNewMask()
    {
        // The identity provider replaced the secret outside any transaction: the service patches the mask with the record.
        var project = NewName("p");
        var storage = new InterceptedRepositoryProvider(Repositories);
        var machines = Machines(storage);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project) with { CredentialKind = MachineCredentialKind.ClientCredentials }, Admin);

        var added = await AddedAsync(AccessPartition(project), async () =>
            (await machines.ResetMachineAccessAsync(Organization, project, machine.Id, ViaMcp)).AccessKey.Should().Be(MachineProviderDouble.SecondClientSecret));

        var record = added.Should().ContainSingle().Subject;
        record.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineReset, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Machine(machine.Id),
            NewValue = "ClientCredentials",
            Detail = "The client secret was replaced.",
        });
        storage.Interceptor.Batches.Should().ContainSingle(candidate => candidate.Contains(BatchOperationKind.Create, record.Id))
            .Which.Operations.Select(operation => (operation.Kind, operation.Id)).Should().Equal((BatchOperationKind.Patch, machine.Id), (BatchOperationKind.Create, record.Id));
        (await Machines(Repositories).GetMachineAccessAsync(Organization, project, machine.Id))!.AccessKey.Should().Be("xy-***");
        (await ReadRawAsync(AccessPartition(project), record.Id)).Should().NotContain(MachineProviderDouble.SecondClientSecret).And.NotContain("xy-");
    }

    [Fact]
    public async Task ResetMachine_ClientCredentials_AFailedBatchHandsOutNoSecretAndStoresNoRecord()
    {
        var project = NewName("p");
        var storage = new InterceptedRepositoryProvider(Repositories);
        var machines = Machines(storage);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project) with { CredentialKind = MachineCredentialKind.ClientCredentials }, Admin);
        storage.Interceptor.BeforeBatch = batch => Task.FromResult<HttpStatusCode?>(
            batch.Contains(BatchOperationKind.Patch, machine.Id) ? HttpStatusCode.ServiceUnavailable : null);

        var added = await AddedAsync(AccessPartition(project), async () =>
        {
            var reset = () => machines.ResetMachineAccessAsync(Organization, project, machine.Id, ViaMcp);
            // the old secret is gone at the identity provider and the new one is withheld: the caller is told to reset again
            (await reset.Should().ThrowAsync<ServiceUnavailableException>()).Which.ErrorCode.Should().Be(ErrorCodes.AccessAuditUnavailable);
        });

        added.Should().BeEmpty();
        (await Machines(Repositories).GetMachineAccessAsync(Organization, project, machine.Id))!.AccessKey.Should().Be("cs-***");
    }

    [Fact]
    public async Task DeleteMachine_RecordsWhatItWas_AndDeletingAgainRecordsNothing()
    {
        var project = NewName("p");
        var machines = Machines(Repositories);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project), Admin);

        var added = await AddedAsync(AccessPartition(project), async () =>
        {
            (await machines.DeleteMachineAccessAsync(Organization, project, machine.Id, ViaMcp)).Should().BeTrue();
            (await machines.DeleteMachineAccessAsync(Organization, project, machine.Id, ViaMcp)).Should().BeFalse();
        });

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineDeleted, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Machine(machine.Id),
            OldValue = AccessAuditValues.Machine(MachineCredentialKind.ApiKey, Scopes, "unit.billing", false, null),
        });
        (await machines.GetMachineAccessAsync(Organization, project, machine.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteMachine_AChangeAfterTheCleanup_IsReadAgain_AndTheRecordDescribesTheDeletedVersion()
    {
        var project = NewName("p");
        var storage = new InterceptedRepositoryProvider(Repositories);
        var machines = Machines(storage);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project), Admin);
        var changed = false;

        // the annotation restriction of the machine changes after the read of the delete and before its batch
        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!changed && batch.Contains(BatchOperationKind.Delete, machine.Id))
            {
                changed = true;
                await Repositories.GetOrganizationContainer(Organization).Container.PatchItemAsync<object>(
                    machine.Id,
                    OrganizationKeys.Key(Organization, AccessPartition(project)),
                    [PatchOperation.Set("/AnnotationKey", "unit.payroll")]);
            }

            return null;
        };

        var added = await AddedAsync(AccessPartition(project), async () =>
            (await machines.DeleteMachineAccessAsync(Organization, project, machine.Id, ViaMcp)).Should().BeTrue());

        storage.Interceptor.Batches.Where(batch => batch.Contains(BatchOperationKind.Delete, machine.Id)).Select(batch => batch.StatusCode)
            .Should().Equal(HttpStatusCode.PreconditionFailed, HttpStatusCode.OK);
        added.Should().ContainSingle().Which.OldValue.Should().Be(AccessAuditValues.Machine(MachineCredentialKind.ApiKey, Scopes, "unit.payroll", false, null));
        (await Machines(Repositories).GetMachineAccessAsync(Organization, project, machine.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteMachine_AFailedBatch_KeepsTheMachineListedAndStoresNoRecord()
    {
        var project = NewName("p");
        var storage = new InterceptedRepositoryProvider(Repositories);
        var machines = Machines(storage);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project), Admin);
        storage.Interceptor.BeforeBatch = batch => Task.FromResult<HttpStatusCode?>(
            batch.Contains(BatchOperationKind.Delete, machine.Id) ? HttpStatusCode.ServiceUnavailable : null);

        var added = await AddedAsync(AccessPartition(project), async () =>
        {
            var delete = () => machines.DeleteMachineAccessAsync(Organization, project, machine.Id, ViaMcp);
            (await delete.Should().ThrowAsync<CosmosException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        });

        added.Should().BeEmpty();
        (await Machines(Repositories).GetMachineAccessesAsync(Organization, project)).Select(listed => listed.Id).Should().Equal(machine.Id);
    }

    [Fact]
    public async Task FederationRules_RecordTheOldAndTheNewRules_AndUnchangedRulesRecordNothing()
    {
        var project = NewName("p");
        var machines = Machines(Repositories);
        IReadOnlyList<FederationRule> first = [Rule("repo:acme/app:*")];
        IReadOnlyList<FederationRule> second = [Rule("repo:acme/app:ref:refs/heads/main"), Rule("repo:acme/tool:*")];
        var machine = await machines.CreateMachineAccessAsync(FederatedMachine(project, first), Admin);

        var added = await AddedAsync(AccessPartition(project), async () =>
        {
            await machines.SetMachineFederationRulesAsync(Organization, project, machine.Id, second, ViaMcp);
            await machines.SetMachineFederationRulesAsync(Organization, project, machine.Id, second.ToList(), ViaMcp);
        });

        var records = await ReadAsync(AccessPartition(project));
        records.Should().HaveCount(2);
        records[0].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineCreated, $"{Organization}.{project}", Admin)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Machine(machine.Id),
            NewValue = AccessAuditValues.Machine(MachineCredentialKind.Federated, Scopes, null, false, null),
            Detail = $"Federation rules: {AccessAuditValues.FederationRules(first)}",
        });
        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineFederationRulesChanged, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Machine(machine.Id),
            OldValue = AccessAuditValues.FederationRules(first),
            NewValue = AccessAuditValues.FederationRules(second),
        });
        added[0].NewValue.Should().Contain("repo:acme/tool:*");
    }

    [Fact]
    public async Task FederationRules_AFailedBatch_KeepsTheRulesAndStoresNoRecord()
    {
        var project = NewName("p");
        var storage = new InterceptedRepositoryProvider(Repositories);
        var machines = Machines(storage);
        var machine = await machines.CreateMachineAccessAsync(FederatedMachine(project, [Rule("repo:acme/app:*")]), Admin);
        storage.Interceptor.BeforeBatch = batch => Task.FromResult<HttpStatusCode?>(
            batch.Contains(BatchOperationKind.Replace, machine.Id) ? HttpStatusCode.ServiceUnavailable : null);

        var added = await AddedAsync(AccessPartition(project), async () =>
        {
            var change = () => machines.SetMachineFederationRulesAsync(Organization, project, machine.Id, [Rule("repo:evil/app:*")], ViaMcp);
            (await change.Should().ThrowAsync<CosmosException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        });

        added.Should().BeEmpty();
        (await Machines(Repositories).GetMachineAccessAsync(Organization, project, machine.Id))!.FederationRules!.Single().Subject.Should().Be("repo:acme/app:*");
    }

    [Fact]
    public async Task GetMachineAccesses_ListsNoRecordAndNoCertificateDocument()
    {
        var project = NewName("p");
        var machines = Machines(Repositories);
        var machine = await machines.CreateMachineAccessAsync(ApiKeyMachine(project), Admin);
        await SharedCertificates().StoreAsync(Organization, project, NewCertificate("edge"), Admin);
        await new CosmosClientCertificateStore(
                Repositories,
                Context.Services.GetRequiredService<IOptions<Newtonsoft.Json.JsonSerializerSettings>>(),
                SerializerOptions,
                new MemoryCache(new MemoryCacheOptions()))
            .StoreAsync(Organization, project, new CertificateRecord(machine.Id, NewThumbprint(), null, null, "serial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), false, Scopes, null, null));

        var listed = await machines.GetMachineAccessesAsync(Organization, project);

        listed.Select(item => item.Id).Should().Equal(machine.Id);
        (await ReadAsync(AccessPartition(project))).Select(record => record.Action).Should().Equal(AccessAuditAction.MachineCreated, AccessAuditAction.SharedCertificateIssued);
    }

    [Fact]
    public async Task TrustedIssuers_CreateUpdateAndDelete_RecordEachChangeOnce_AndTheListShowsNoRecord()
    {
        var project = NewName("p");
        var partition = CosmosFederatedIssuerStore.GetPartition(project);
        var issuers = Issuers();
        var issuer = new FederatedIssuer
        {
            Name = "github",
            Issuer = "https://token.actions.githubusercontent.com",
            KeySource = FederatedIssuerKeySource.Jwks,
            Jwks = TwoKeys,
            RequiredClaims = new Dictionary<string, IReadOnlyList<string>> { ["repository_owner_id"] = ["42"] },
            Description = "CI",
        };
        var updated = issuer with { Description = "CI of the company", MaxTokenLifetimeSeconds = 600 };

        var changes = await AddedAsync(partition, async () =>
        {
            await issuers.SetAsync(Organization, project, issuer, ViaMcp);
            await issuers.SetAsync(Organization, project, issuer, ViaMcp);
            await issuers.SetAsync(Organization, project, updated, Admin);
        });

        changes.Should().HaveCount(2);
        changes[0].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.FederatedIssuerCreated, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = partition,
            Target = AccessAuditTarget.Issuer("github"),
            NewValue = AccessAuditValues.Issuer(issuer),
        });
        changes[0].NewValue.Should().Contain("\"JwksKeys\":2").And.NotContain(Modulus).And.Contain("repository_owner_id");
        changes[1].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.FederatedIssuerUpdated, $"{Organization}.{project}", Admin)
        {
            PartitionKey = partition,
            Target = AccessAuditTarget.Issuer("github"),
            OldValue = AccessAuditValues.Issuer(issuer),
            NewValue = AccessAuditValues.Issuer(updated),
        });
        (await Issuers().ListAsync(Organization, project)).Select(listed => listed.Name).Should().Equal("github");

        var deletes = await AddedAsync(partition, async () =>
        {
            (await issuers.DeleteAsync(Organization, project, "github", ViaMcp)).Should().BeTrue();
            (await issuers.DeleteAsync(Organization, project, "github", ViaMcp)).Should().BeFalse();
        });

        deletes.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.FederatedIssuerDeleted, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = partition,
            Target = AccessAuditTarget.Issuer("github"),
            OldValue = AccessAuditValues.Issuer(updated),
        });
        (await Issuers().ListAsync(Organization, project)).Should().BeEmpty();
    }

    [Fact]
    public async Task TrustedIssuers_AKeyReplacedByAnother_IsRecorded_AReformattedKeySetIsNot()
    {
        var project = NewName("p");
        var partition = CosmosFederatedIssuerStore.GetPartition(project);
        var issuers = Issuers();
        var issuer = new FederatedIssuer { Name = "k8s", Issuer = "https://k8s.example.com", KeySource = FederatedIssuerKeySource.Jwks, Jwks = TwoKeys };
        var swapped = issuer with { Jwks = TwoKeys.Replace("\"kid\":\"two\"", "\"kid\":\"three\"", StringComparison.Ordinal) };
        var reformatted = swapped with { Jwks = swapped.Jwks!.Replace(",", ", ", StringComparison.Ordinal) };
        await issuers.SetAsync(Organization, project, issuer, Admin);

        var changes = await AddedAsync(partition, async () =>
        {
            await issuers.SetAsync(Organization, project, swapped, ViaMcp);
            await issuers.SetAsync(Organization, project, reformatted, ViaMcp);
        });

        changes.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.FederatedIssuerUpdated, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = partition,
            Target = AccessAuditTarget.Issuer("k8s"),
            OldValue = AccessAuditValues.Issuer(issuer),
            NewValue = AccessAuditValues.Issuer(swapped),
        });
        changes[0].NewValue.Should().Contain("\"JwksKeys\":2").And.NotContain(Modulus);
    }

    [Fact]
    public async Task TrustedIssuers_ADeleteRacingAnUpdate_RecordsTheVersionItDeleted()
    {
        var project = NewName("p");
        var partition = CosmosFederatedIssuerStore.GetPartition(project);
        var issuer = new FederatedIssuer { Name = "github", Issuer = "https://token.actions.githubusercontent.com", Description = "CI" };
        var updated = issuer with { Description = "CI of the company" };
        await Issuers().SetAsync(Organization, project, issuer, Admin);
        var storage = new InterceptedRepositoryProvider(Repositories);
        var racing = new CosmosFederatedIssuerStore(storage, SerializerOptions, new MemoryCache(new MemoryCacheOptions()));
        var raced = false;

        // another Administrator saves the issuer between the delete's read and its batch
        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!raced && batch.Contains(BatchOperationKind.Delete, "github"))
            {
                raced = true;
                await Issuers().SetAsync(Organization, project, updated, Admin);
            }

            return null;
        };

        var changes = await AddedAsync(partition, async () =>
            (await racing.DeleteAsync(Organization, project, "github", ViaMcp)).Should().BeTrue());

        changes.Select(change => change.Action).Should().Equal(AccessAuditAction.FederatedIssuerUpdated, AccessAuditAction.FederatedIssuerDeleted);
        changes[1].OldValue.Should().Be(AccessAuditValues.Issuer(updated), "the record describes the version the delete removed");
        storage.Interceptor.Batches.Select(batch => batch.StatusCode).Should().Equal(HttpStatusCode.PreconditionFailed, HttpStatusCode.OK);
        (await Issuers().ListAsync(Organization, project)).Should().BeEmpty();
    }

    [Fact]
    public async Task TrustedIssuers_TheLimitCountsNoRecord()
    {
        // every issuer created writes a record next to it: counted, they would refuse the issuer after half the limit
        var project = NewName("p");
        var issuers = Issuers();

        for (var index = 1; index <= FederationValidation.MaxIssuersPerProject; index++)
        {
            await issuers.SetAsync(Organization, project, new FederatedIssuer { Name = $"issuer-{index:D2}", Issuer = $"https://issuer-{index:D2}.example.com" }, Admin);
        }

        var overTheLimit = () => issuers.SetAsync(Organization, project, new FederatedIssuer { Name = "issuer-99", Issuer = "https://issuer-99.example.com" }, Admin);

        (await overTheLimit.Should().ThrowAsync<InvalidInputException>()).Which.Message.Should().Be(FederationValidation.TooManyIssuersMessage);
        (await Issuers().ListAsync(Organization, project)).Should().HaveCount(FederationValidation.MaxIssuersPerProject);
        (await ReadAsync(CosmosFederatedIssuerStore.GetPartition(project))).Should().HaveCount(FederationValidation.MaxIssuersPerProject);
    }

    [Fact]
    public async Task SharedCertificates_IssueAndRevoke_RecordOnce_NeverThePfxOrItsPassword()
    {
        var project = NewName("p");
        var store = SharedCertificates();
        var certificate = NewCertificate("edge") with { Certificate = "MIIpfx-content", CertificatePassword = "pfx-password" };

        var added = await AddedAsync(AccessPartition(project), async () =>
        {
            await store.StoreAsync(Organization, project, certificate, ViaMcp);
            (await store.RevokeAsync(Organization, project, certificate.Thumbprint, Admin)).Should().BeTrue();
            (await store.RevokeAsync(Organization, project, certificate.Thumbprint, Admin)).Should().BeTrue("a revoked certificate stays revoked");
        });

        added.Should().HaveCount(2);
        added[0].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.SharedCertificateIssued, $"{Organization}.{project}", ViaMcp)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Certificate(certificate.Thumbprint, "edge"),
            NewValue = "{\"Label\":\"edge\",\"NotBefore\":\"2026-01-01T00:00:00+00:00\",\"NotAfter\":\"2027-01-01T00:00:00+00:00\"}",
        });
        added[1].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.SharedCertificateRevoked, $"{Organization}.{project}", Admin)
        {
            PartitionKey = AccessPartition(project),
            Target = AccessAuditTarget.Certificate(certificate.Thumbprint, "edge"),
            OldValue = AccessAuditValues.Certificate(certificate),
            NewValue = AccessAuditValues.Certificate(certificate with { IsRevoked = true }),
        });
        added[1].NewValue.Should().Contain("\"IsRevoked\":true");

        foreach (var record in added)
        {
            (await ReadRawAsync(AccessPartition(project), record.Id)).Should().NotContain("MIIpfx-content").And.NotContain("pfx-password");
        }
    }

    private static string NewName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private static string NewThumbprint() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    private static string AccessPartition(string project) => $"{project}.access";

    private static FederationRule Rule(string subject) => new()
    {
        Issuer = "github",
        Audience = "urn:storyteller:test.platform:api",
        Subject = subject,
    };

    private static MachineAccessCreate ApiKeyMachine(string project) => new()
    {
        Organization = Organization,
        Project = project,
        Scopes = Scopes,
        AnnotationKey = "unit.billing",
        CredentialKind = MachineCredentialKind.ApiKey,
    };

    private static MachineAccessCreate FederatedMachine(string project, IReadOnlyList<FederationRule> rules) => new()
    {
        Organization = Organization,
        Project = project,
        Scopes = Scopes,
        CredentialKind = MachineCredentialKind.Federated,
        FederationRules = rules,
    };

    private static SharedCertificate NewCertificate(string label) => new()
    {
        Thumbprint = NewThumbprint(),
        Label = label,
        NotBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        NotAfter = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private CosmosMergedApiKeyHashStore HashStore(IContainerRepositoryProvider repositories) => new(repositories, SerializerOptions);

    // The access service over the given storage, with the machine provider in short over the hash store of the same storage.
    private CosmosAccessService Machines(IContainerRepositoryProvider repositories) => new(
        repositories,
        SerializerOptions,
        new MachineProviderDouble(HashStore(repositories)),
        Context.Services.GetRequiredService<AccessAuditRecords>());

    // Fresh stores, fresh caches: every list comes from Cosmos DB.
    private CosmosFederatedIssuerStore Issuers() => new(Repositories, SerializerOptions, new MemoryCache(new MemoryCacheOptions()));

    private CosmosSharedCertificateStore SharedCertificates() => new(Repositories, SerializerOptions, new MemoryCache(new MemoryCacheOptions()));

    private Task<IReadOnlyList<AccessAuditEntity>> ReadAsync(string partition)
    {
        return StoredAccessAudit.ReadAsync(Repositories, Organization, partition);
    }

    private Task<IReadOnlyList<AccessAuditEntity>> AddedAsync(string partition, Func<Task> change)
    {
        return StoredAccessAudit.AddedByAsync(() => ReadAsync(partition), change);
    }

    // The record as stored, every field of it.
    private async Task<string> ReadRawAsync(string partition, string id)
    {
        var repository = Repositories.GetOrganizationContainer(Organization);
        using var response = await repository.Container.ReadItemStreamAsync(id, repository.Key(partition));
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(response.Content);
        return document.RootElement.GetRawText();
    }

    // The machine providers in short: an API key machine stores its hash first and replaces it on a reset, over the
    // Cosmos hash store, as ApiKeyMachineAccessService does; a ClientCredentials machine gets a new secret from its
    // identity provider without any write of ours; a Federated machine has no secret.
    private sealed class MachineProviderDouble(IApiKeyHashStore hashStore) : IMachineAccessService
    {
        public const string FirstKey = "2s.key-1-secret";
        public const string SecondKey = "2s.key-2-secret";
        public const string FirstHash = "hash-1-of-the-key";
        public const string SecondHash = "hash-2-of-the-key";
        public const string FirstClientSecret = "cs-secret-1";
        public const string SecondClientSecret = "xy-secret-2";

        public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
        {
            var id = Guid.NewGuid().ToString("D");
            var kind = model.CredentialKind ?? MachineCredentialKind.ApiKey;
            var scopes = model.Scopes ?? [];

            if (kind.HasApiKey())
            {
                await hashStore.StoreAsync(model.Organization, model.Project, id, FirstHash, scopes);
            }

            return new MachineAccess
            {
                Id = id,
                ObjectId = id,
                AccessKey = kind switch
                {
                    MachineCredentialKind.ClientCredentials => FirstClientSecret,
                    MachineCredentialKind.Federated => string.Empty,
                    _ => FirstKey,
                },
                Scopes = scopes,
                AnnotationKey = model.AnnotationKey,
                CredentialKind = kind,
                FederationRules = model.FederationRules,
            };
        }

        public async Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
        {
            if (existingAccess.CredentialKind == MachineCredentialKind.ClientCredentials)
            {
                return SecondClientSecret;
            }

            var existing = await hashStore.GetAsync(organization, project, existingAccess.Id);
            return existing is not null && await hashStore.ReplaceAsync(organization, project, existingAccess.Id, existing.HashedSecret, SecondHash)
                ? SecondKey
                : null;
        }

        public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project) => throw new NotSupportedException();

        public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
        {
            return hashStore.DeleteAsync(organization, project, objectId);
        }
    }
}
