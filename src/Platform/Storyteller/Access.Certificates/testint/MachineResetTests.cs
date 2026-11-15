using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// The reset of a machine against real storage. It changes only the secret. A concurrent writer runs at fixed points:
/// before the router, before the hash replace, after the router, before the read-back of the record, and inside the hash
/// store between its read and its patch. A delete there, finished or with only the hash cleared, answers 404, brings
/// no document back and leaves no working key. A renewal there is kept. A reset that finds another reset's hash
/// answers 409 and keeps the other one.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MachineResetTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "resetproj";
    private const string AnnotationKey = "sbt.north";

    private static readonly MachineScope[] Granted = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead];

    private static readonly PartitionKey PartitionKey = OrganizationKeys.Key(Organization, $"{Project}.access");

    // Who resets, with the invocation of the request: the access audit record keeps both.
    private static readonly AccessCaller Admin = AccessCaller.Account("admin-1", "Ada Admin") with { CorrelationId = "invocation-reset" };

    private IApiKeyValidator Validator => fixture.Services.GetRequiredService<IApiKeyValidator>();

    private IApiKeyHashStore HashStore => fixture.Services.GetRequiredService<IApiKeyHashStore>();

    private Container Container => fixture.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Organization)
        .Container;

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true)]
    public async Task Reset_OfAnApiKey_ChangesOnlyTheHash(MachineCredentialKind kind, bool useSharedCertificate)
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(kind) with { UseSharedCertificate = useSharedCertificate }, AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        var after = await ReadDocumentAsync(machine.Id);
        ChangedProperties(before!, after!).ShouldBe([nameof(MachineAccessEntity.HashedSecret)]);
        after![nameof(MachineAccessEntity.AccessKey)]!.GetValue<string>().ShouldBe("2s.***");

        // The response is the stored record with the new key.
        reset.CredentialKind.ShouldBe(kind);
        reset.Scopes.ShouldBe(Granted);
        reset.AnnotationKey.ShouldBe(AnnotationKey);
        reset.UsesSharedCertificate.ShouldBe(useSharedCertificate);
        reset.CertificateThumbprint.ShouldBe(machine.CertificateThumbprint);
        reset.CertificateNotAfter.ShouldBe(machine.CertificateNotAfter);

        // The new key works with the stored scopes, and the old one is refused.
        (await Validator.ValidateAsync(reset.AccessKey)).ShouldNotBeNull().Scopes.ShouldBe(Granted);
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();

        if (kind == MachineCredentialKind.CertificateAndApiKey && !useSharedCertificate)
        {
            // The own certificate is untouched: its record is there, and it still signs in.
            var record = await fixture.Services.GetRequiredService<IClientCertificateStore>().GetByMachineAccessIdAsync(Organization, Project, machine.Id);
            record.ShouldNotBeNull().Thumbprint.ShouldBe(machine.CertificateThumbprint);
            var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(machine.Certificate!), machine.CertificatePassword);
            (await fixture.Services.GetRequiredService<IClientCertificateValidator>().ValidateAsync(certificate)).ShouldNotBeNull().MachineAccessId.ShouldBe(machine.Id);
        }
    }

    [Fact]
    public async Task Reset_OfACertificateMachine_IsNotSupported_AndWritesNothing()
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.Certificate), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        exception.Message.ShouldBe(PolicyAwareMachineAccessService.CertificateResetNotSupportedMessage);
        exception.ErrorCode.ShouldBe(ErrorCodes.MachineAccessNotSupported);
        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(before!));
        (await ResetRecordsAsync(machine.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_OfAClientCredentialsMachine_ChangesOnlyTheMaskedKey()
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.ClientCredentials), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        var after = await ReadDocumentAsync(machine.Id);
        reset.AccessKey.ShouldBe($"reset-secret-{machine.ObjectId}");
        ChangedProperties(before!, after!).ShouldBe([nameof(MachineAccessEntity.AccessKey)]);
        before![nameof(MachineAccessEntity.AccessKey)]!.GetValue<string>().ShouldBe("sec***");
        after![nameof(MachineAccessEntity.AccessKey)]!.GetValue<string>().ShouldBe("res***");
    }

    // The API key service finds no hash. The access service read one, so the machine was deleted meanwhile.
    [Fact]
    public async Task Reset_DeletedBeforeTheRouter_AnswersNotFound_AndBringsNothingBack()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var router = new InterleavingRouter(fixture.Services.GetRequiredService<IMachineAccessService>())
        {
            BeforeReset = () => DeleteAsync(machine.Id),
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(router).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        exception.Message.ShouldBe(DeletedDuringItsReset(machine.Id));
        router.IssuedKey.ShouldBeNull();
        (await ReadDocumentAsync(machine.Id)).ShouldBeNull();
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();
    }

    [Fact]
    public async Task Reset_DeletedBeforeTheHashReplace_AnswersNotFound_AndBringsNothingBack()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var hashStore = new InterleavingHashStore(HashStore)
        {
            BeforeReplace = () => DeleteAsync(machine.Id),
        };

        await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(hashStore).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        (await ReadDocumentAsync(machine.Id)).ShouldBeNull();
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();
    }

    // 424-86: before the fix, the reset wrote its stale copy back and the old key worked again.
    [Fact]
    public async Task Reset_DeletedAfterTheRouter_AnswersNotFound_AndBringsNothingBack()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var router = new InterleavingRouter(fixture.Services.GetRequiredService<IMachineAccessService>())
        {
            AfterReset = () => DeleteAsync(machine.Id),
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(router).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        exception.Message.ShouldBe(DeletedDuringItsReset(machine.Id));
        (await ReadDocumentAsync(machine.Id)).ShouldBeNull();
        router.IssuedKey.ShouldNotBeNull();
        (await Validator.ValidateAsync(router.IssuedKey)).ShouldBeNull();
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();
    }

    // A delete in progress: the hash is cleared, the document is still there. The reset writes nothing.
    [Fact]
    public async Task Reset_HashClearedByADeleteBeforeTheRouter_AnswersNotFound_AndWritesNothing()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);
        JsonObject? cleared = null;
        var router = new InterleavingRouter(fixture.Services.GetRequiredService<IMachineAccessService>())
        {
            BeforeReset = async () => cleared = await ClearTheHashAsync(machine.Id),
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(router).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        exception.Message.ShouldBe(DeletedDuringItsReset(machine.Id));
        router.IssuedKey.ShouldBeNull();
        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(cleared!));
        await ShouldHoldOnlyTheClearedHashAsync(before!, machine, router.IssuedKey);
        (await ResetRecordsAsync(machine.Id)).ShouldBeEmpty();
    }

    // A delete that cleared the hash before the reset read the machine, or stopped after it (as the compensation of a
    // failed create does): the record has no key to replace, and the reset answers 404, not 500 (424-102). It writes
    // nothing.
    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    public async Task Reset_HashClearedBeforeTheRead_AnswersNotFound_AndWritesNothing(MachineCredentialKind kind)
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(kind), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);
        var cleared = await ClearTheHashAsync(machine.Id);

        var exception = await Should.ThrowAsync<NotFoundException>(() => accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        exception.Message.ShouldBe($"The machine access {machine.Id} has no API key to reset; it is being deleted, or its creation failed. Delete it and create a new one.");
        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(cleared));
        await ShouldHoldOnlyTheClearedHashAsync(before!, machine, issuedKey: null);
        (await ResetRecordsAsync(machine.Id)).ShouldBeEmpty();
    }

    // A ClientCredentials machine never has a hash. Its identity provider giving no secret is a failure there, not a
    // delete, so the 404 above stays with the kinds that have an API key.
    [Fact]
    public async Task Reset_OfAClientCredentialsMachineWithoutASecret_IsNotAnsweredAsDeleted()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ClientCredentials), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateAccessService(new SecretlessRouter()).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        exception.Message.ShouldBe($"The machine access {machine.Id} reset failed.");
        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(before!));
        (await ResetRecordsAsync(machine.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_HashClearedByADeleteBeforeTheHashReplace_AnswersNotFound_AndWritesNothing()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);
        JsonObject? cleared = null;
        var hashStore = new InterleavingHashStore(HashStore)
        {
            BeforeReplace = async () => cleared = await ClearTheHashAsync(machine.Id),
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(hashStore).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        // The hash store's own answer: it finds no hash to replace.
        exception.Message.ShouldBe($"The machine access {machine.Id} has not been found.");
        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(cleared!));
        await ShouldHoldOnlyTheClearedHashAsync(before!, machine, issuedKey: null);
        (await ResetRecordsAsync(machine.Id)).ShouldBeEmpty();
    }

    // The new hash was written, then the delete cleared it. Answering 200 would hand out a key that never works.
    [Fact]
    public async Task Reset_HashClearedByADeleteAfterTheRouter_AnswersNotFound_AndLeavesNoWorkingKey()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);
        var router = new InterleavingRouter(fixture.Services.GetRequiredService<IMachineAccessService>())
        {
            AfterReset = () => ClearTheHashAsync(machine.Id),
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(router).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        // The masked-key patch did run, but it wrote the 2s.*** that was already there.
        exception.Message.ShouldBe(DeletedDuringItsReset(machine.Id));
        router.IssuedKey.ShouldNotBeNull();
        await ShouldHoldOnlyTheClearedHashAsync(before!, machine, router.IssuedKey);
    }

    [Fact]
    public async Task Reset_RenewedBeforeTheHashReplace_KeepsTheRenewal()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(RenewableModel(), AccessCaller.System);
        string? renewedThumbprint = null;
        JsonObject? renewed = null;
        var hashStore = new InterleavingHashStore(HashStore)
        {
            BeforeReplace = async () =>
            {
                renewedThumbprint = await RenewAsync(machine);
                renewed = await ReadDocumentAsync(machine.Id);
            },
        };

        var reset = await CreateAccessService(hashStore).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        ChangedProperties(renewed!, (await ReadDocumentAsync(machine.Id))!).ShouldBe([nameof(MachineAccessEntity.HashedSecret)]);
        await ShouldKeepTheRenewalAsync(machine, renewedThumbprint!, reset);
    }

    [Fact]
    public async Task Reset_RenewedAfterTheRouter_KeepsTheRenewal()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(RenewableModel(), AccessCaller.System);
        string? renewedThumbprint = null;
        JsonObject? renewed = null;
        var router = new InterleavingRouter(fixture.Services.GetRequiredService<IMachineAccessService>())
        {
            AfterReset = async () =>
            {
                renewedThumbprint = await RenewAsync(machine);
                renewed = await ReadDocumentAsync(machine.Id);
            },
        };

        var reset = await CreateAccessService(router).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        // The masked key was already 2s.***, so the patch leaves the renewed document as it was.
        ChangedProperties(renewed!, (await ReadDocumentAsync(machine.Id))!).ShouldBeEmpty();
        await ShouldKeepTheRenewalAsync(machine, renewedThumbprint!, reset);
    }

    // After every read of the access service, right before its write. A write of a copy read earlier (a re-read and
    // an upsert, say) would put the old certificate back; the strict container refuses any write but a patch or a batch,
    // and records both.
    [Fact]
    public async Task Reset_RenewedBeforeTheReadBack_KeepsTheRenewal_AndWritesNothingAfterTheHash()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(RenewableModel(), AccessCaller.System);
        string? renewedThumbprint = null;
        JsonObject? renewed = null;
        var reads = 0;
        var statuses = new List<HttpStatusCode>();
        var repositoryProvider = CreateInterleavingProvider(
            () => Task.CompletedTask,
            statuses,
            async () =>
            {
                // The first read is the reset's own; the second reads the record back after the hash replace.
                if (++reads == 2)
                {
                    renewedThumbprint = await RenewAsync(machine);
                    renewed = await ReadDocumentAsync(machine.Id);
                }
            });

        // The real router, so the real hash store over the real container; only the access service is wrapped.
        var accessService = CreateAccessService(fixture.Services.GetRequiredService<IMachineAccessService>(), repositoryProvider);
        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        // The mask of an API key does not change, so the reset writes nothing after the hash: its record went with
        // the hash, in the batch of the hash store.
        statuses.ShouldBeEmpty();
        reads.ShouldBe(2);
        ChangedProperties(renewed!, (await ReadDocumentAsync(machine.Id))!).ShouldBeEmpty();
        await ShouldKeepTheRenewalAsync(machine, renewedThumbprint!, reset);
        (await ResetRecordsAsync(machine.Id)).ShouldHaveSingleItem();
    }

    // Only the winner is recorded: the loser changed nothing.
    [Fact]
    public async Task Reset_AnotherResetWonTheHash_AnswersConflict_AndKeepsTheWinner()
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        MachineAccess? winner = null;
        JsonObject? won = null;
        var hashStore = new InterleavingHashStore(HashStore)
        {
            BeforeReplace = async () =>
            {
                winner = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, Admin);
                won = await ReadDocumentAsync(machine.Id);
            },
        };

        await Should.ThrowAsync<ConflictException>(() => CreateAccessService(hashStore).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(won!));
        (await Validator.ValidateAsync(winner!.AccessKey)).ShouldNotBeNull();
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();
        (await ResetRecordsAsync(machine.Id)).ShouldHaveSingleItem().Record.Actor.ShouldBe(Admin.Actor);
    }

    // The access audit record of an API key's reset: one, written in the batch of the new hash, with what was replaced
    // and never the key or a hash. A CertificateAndApiKey machine resets only its API key.
    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true)]
    public async Task Reset_OfAnApiKey_RecordsOneReset_WithoutTheKeyOrAHash(MachineCredentialKind kind, bool useSharedCertificate)
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(kind) with { UseSharedCertificate = useSharedCertificate }, AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, Admin);

        var after = await ReadDocumentAsync(machine.Id);
        var record = (await ResetRecordsAsync(machine.Id)).ShouldHaveSingleItem();
        record.ShouldBeBy(Admin, Organization, Project, $"{Project}.access");
        record.Record.Target.ShouldBe(AccessAuditTarget.Machine(machine.Id));
        record.Record.OldValue.ShouldBeNull();
        record.Record.NewValue.ShouldBe(kind.ToString());
        record.Record.Detail.ShouldBe("The API key was replaced.");
        record.ShouldHoldNoSecret(machine.AccessKey, reset.AccessKey, Hash(before!), Hash(after!), machine.Certificate, machine.CertificatePassword);
    }

    [Fact]
    public async Task Reset_Twice_RecordsEachReset()
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);

        await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, Admin);
        await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        var records = await ResetRecordsAsync(machine.Id);
        records.Select(stored => stored.Record.Actor).ShouldBe([Admin.Actor, AuditActor.System]);
        records.Select(stored => stored.Record.Id).Distinct().Count().ShouldBe(2);
    }

    // A renewal between the hash store's read and its batch only moves the ETag: the batch of the hash and the record
    // fails as a whole (412), and its retry stores both, once.
    [Fact]
    public async Task Reset_RenewedBetweenTheReadAndTheBatchOfTheHash_RetriesAndRecordsOnce()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(RenewableModel(), AccessCaller.System);
        var statuses = new List<HttpStatusCode>();
        string? renewedThumbprint = null;
        var hashStore = CreateHashStore(FirstOnly(async () => renewedThumbprint = await RenewAsync(machine)), statuses);

        var reset = await CreateAccessService(hashStore).ResetMachineAccessAsync(Organization, Project, machine.Id, Admin);

        statuses.ShouldBe([HttpStatusCode.PreconditionFailed, HttpStatusCode.OK]);
        await ShouldKeepTheRenewalAsync(machine, renewedThumbprint!, reset);
        (await ResetRecordsAsync(machine.Id)).ShouldHaveSingleItem().Record.Actor.ShouldBe(Admin.Actor);
    }

    // Another reset lands between the hash store's read and its batch: the batch fails on the ETag (412), the store reads
    // the winner's hash, and the loser answers 409 without a record.
    [Fact]
    public async Task Reset_LosingAtTheBatchOfTheHash_AnswersConflict_AndRecordsOnlyTheWinner()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var statuses = new List<HttpStatusCode>();
        MachineAccess? winner = null;
        var hashStore = CreateHashStore(
            FirstOnly(async () => winner = await fixture.Services.GetRequiredService<IAccessService>().ResetMachineAccessAsync(Organization, Project, machine.Id, Admin)),
            statuses);

        await Should.ThrowAsync<ConflictException>(() => CreateAccessService(hashStore).ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System));

        statuses.ShouldBe([HttpStatusCode.PreconditionFailed]);
        (await ResetRecordsAsync(machine.Id)).ShouldHaveSingleItem().Record.Actor.ShouldBe(Admin.Actor);
        (await Validator.ValidateAsync(winner!.AccessKey)).ShouldNotBeNull();
    }

    // The identity provider replaced the secret outside any transaction of ours: the service patches the mask in one batch
    // with the record, always, also when the mask stays the same, before it hands the secret out.
    [Fact]
    public async Task Reset_OfAClientCredentialsMachine_RecordsEveryReset_WithTheMaskPatched()
    {
        var accessService = await PrepareAsync();
        var machine = await accessService.CreateMachineAccessAsync(Model(MachineCredentialKind.ClientCredentials), AccessCaller.System);

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, Admin);
        var first = await ReadDocumentAsync(machine.Id);
        await accessService.ResetMachineAccessAsync(Organization, Project, machine.Id, AccessCaller.System);

        // The recording provider issues the same secret again, so the second patch keeps the mask.
        first![nameof(MachineAccessEntity.AccessKey)]!.GetValue<string>().ShouldBe("res***");
        ETag((await ReadDocumentAsync(machine.Id))!).ShouldNotBe(ETag(first));
        var records = await ResetRecordsAsync(machine.Id);
        records.Select(stored => stored.Record.Actor).ShouldBe([Admin.Actor, AuditActor.System]);
        records[0].ShouldBeBy(Admin, Organization, Project, $"{Project}.access");
        records[0].Record.Target.ShouldBe(AccessAuditTarget.Machine(machine.Id));
        records[0].Record.NewValue.ShouldBe(nameof(MachineCredentialKind.ClientCredentials));
        records[0].Record.Detail.ShouldBe("The client secret was replaced.");
        records[0].ShouldHoldNoSecret(machine.AccessKey, reset.AccessKey);
    }

    // The batch of the mask and the record finds no machine: neither is stored, and the new secret never leaves.
    [Fact]
    public async Task Reset_OfAClientCredentialsMachineDeletedAfterTheRouter_AnswersNotFound_AndRecordsNothing()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ClientCredentials), AccessCaller.System);
        var router = new InterleavingRouter(fixture.Services.GetRequiredService<IMachineAccessService>())
        {
            AfterReset = () => DeleteAsync(machine.Id),
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CreateAccessService(router).ResetMachineAccessAsync(Organization, Project, machine.Id, Admin));

        exception.Message.ShouldBe(DeletedDuringItsReset(machine.Id));
        (await ReadDocumentAsync(machine.Id)).ShouldBeNull();
        (await ResetRecordsAsync(machine.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task HashStore_ReplaceOverAStaleHash_ReturnsFalse_AndKeepsTheStoredHash()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        (await HashStore.ReplaceAsync(Organization, Project, machine.Id, "stale-hash", "new-hash")).ShouldBeFalse();

        ETag((await ReadDocumentAsync(machine.Id))!).ShouldBe(ETag(before!));
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldNotBeNull();
    }

    [Fact]
    public async Task HashStore_ReplaceOnAMissingMachine_ThrowsNotFound_AndCreatesNothing()
    {
        await PrepareAsync();
        var id = Guid.NewGuid().ToString("D");

        await Should.ThrowAsync<NotFoundException>(() => HashStore.ReplaceAsync(Organization, Project, id, "current-hash", "new-hash"));

        (await ReadDocumentAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task HashStore_DeleteOnAMissingMachine_CreatesNothing()
    {
        await PrepareAsync();
        var id = Guid.NewGuid().ToString("D");

        (await HashStore.DeleteAsync(Organization, Project, id)).ShouldBeTrue();

        (await ReadDocumentAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task HashStore_DeleteOnAStoredMachine_ClearsOnlyTheHash()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        (await HashStore.DeleteAsync(Organization, Project, machine.Id)).ShouldBeTrue();

        ChangedProperties(before!, (await ReadDocumentAsync(machine.Id))!).ShouldBe([nameof(MachineAccessEntity.HashedSecret)]);
        (await HashStore.GetAsync(Organization, Project, machine.Id)).ShouldBeNull();
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();
    }

    // The 412 retry: a renewal between the store's own read and its patch only moved the ETag.
    [Fact]
    public async Task HashStore_ReplaceRenewedBetweenItsReadAndPatch_RetriesAndKeepsTheRenewal()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(RenewableModel(), AccessCaller.System);
        var stored = await HashStore.GetAsync(Organization, Project, machine.Id);
        var statuses = new List<HttpStatusCode>();
        string? renewedThumbprint = null;
        var hashStore = CreateHashStore(FirstOnly(async () => renewedThumbprint = await RenewAsync(machine)), statuses);

        (await hashStore.ReplaceAsync(Organization, Project, machine.Id, stored!.HashedSecret, "new-hash")).ShouldBeTrue();

        statuses.ShouldBe([HttpStatusCode.PreconditionFailed, HttpStatusCode.OK]);
        var document = (await ReadDocumentAsync(machine.Id))!;
        document[nameof(MachineAccessEntity.HashedSecret)]!.GetValue<string>().ShouldBe("new-hash");
        document[nameof(MachineAccessEntity.CertificateThumbprint)]!.GetValue<string>().ShouldBe(renewedThumbprint);
    }

    [Fact]
    public async Task HashStore_ReplaceOverAHashChangedBetweenItsReadAndPatch_ReturnsFalse()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var stored = await HashStore.GetAsync(Organization, Project, machine.Id);
        var statuses = new List<HttpStatusCode>();
        var hashStore = CreateHashStore(
            FirstOnly(async () => (await HashStore.ReplaceAsync(Organization, Project, machine.Id, stored!.HashedSecret, "other-hash")).ShouldBeTrue()),
            statuses);

        (await hashStore.ReplaceAsync(Organization, Project, machine.Id, stored!.HashedSecret, "new-hash")).ShouldBeFalse();

        statuses.ShouldBe([HttpStatusCode.PreconditionFailed]);
        (await HashStore.GetAsync(Organization, Project, machine.Id))!.HashedSecret.ShouldBe("other-hash");
    }

    [Fact]
    public async Task HashStore_ReplaceChangedBeforeEveryPatch_IsAConflict()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var stored = await HashStore.GetAsync(Organization, Project, machine.Id);
        var statuses = new List<HttpStatusCode>();
        var hashStore = CreateHashStore(() => TouchAsync(machine.Id), statuses);

        await Should.ThrowAsync<ConflictException>(() => hashStore.ReplaceAsync(Organization, Project, machine.Id, stored!.HashedSecret, "new-hash"));

        statuses.ShouldBe([HttpStatusCode.PreconditionFailed, HttpStatusCode.PreconditionFailed, HttpStatusCode.PreconditionFailed]);
        (await HashStore.GetAsync(Organization, Project, machine.Id))!.HashedSecret.ShouldBe(stored!.HashedSecret);
    }

    // The patch-404 branch: the ETag patch of a document deleted since the read answers 404, not 412.
    [Fact]
    public async Task HashStore_ReplaceDeletedBetweenItsReadAndPatch_ThrowsNotFound_AndCreatesNothing()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var stored = await HashStore.GetAsync(Organization, Project, machine.Id);
        var statuses = new List<HttpStatusCode>();
        var hashStore = CreateHashStore(FirstOnly(() => DeleteAsync(machine.Id)), statuses);

        await Should.ThrowAsync<NotFoundException>(() => hashStore.ReplaceAsync(Organization, Project, machine.Id, stored!.HashedSecret, "new-hash"));

        statuses.ShouldBe([HttpStatusCode.NotFound]);
        (await ReadDocumentAsync(machine.Id)).ShouldBeNull();
    }

    // A delete in progress between the store's own read and its patch: the cleared hash moved the ETag (412), and
    // the second read finds no hash.
    [Fact]
    public async Task HashStore_ReplaceWhoseHashIsClearedBetweenItsReadAndPatch_ThrowsNotFound()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var stored = await HashStore.GetAsync(Organization, Project, machine.Id);
        var statuses = new List<HttpStatusCode>();
        JsonObject? cleared = null;
        var hashStore = CreateHashStore(FirstOnly(async () => cleared = await ClearTheHashAsync(machine.Id)), statuses);

        await Should.ThrowAsync<NotFoundException>(() => hashStore.ReplaceAsync(Organization, Project, machine.Id, stored!.HashedSecret, "new-hash"));

        statuses.ShouldBe([HttpStatusCode.PreconditionFailed]);
        var after = (await ReadDocumentAsync(machine.Id)).ShouldNotBeNull();
        ETag(after).ShouldBe(ETag(cleared!));
        after[nameof(MachineAccessEntity.HashedSecret)].ShouldBeNull();
    }

    // The patch-404 branch of the delete: deleted since the read, so there is no hash left to clear.
    [Fact]
    public async Task HashStore_DeleteOnAMachineDeletedBetweenItsReadAndPatch_ReturnsTrue_AndCreatesNothing()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey), AccessCaller.System);
        var statuses = new List<HttpStatusCode>();
        var hashStore = CreateHashStore(FirstOnly(() => DeleteAsync(machine.Id)), statuses);

        (await hashStore.DeleteAsync(Organization, Project, machine.Id)).ShouldBeTrue();

        statuses.ShouldBe([HttpStatusCode.NotFound]);
        (await ReadDocumentAsync(machine.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task HashStore_StoreOnAStoredMachine_ChangesOnlyTheHashAndTheScopes()
    {
        var machine = await (await PrepareAsync()).CreateMachineAccessAsync(Model(MachineCredentialKind.CertificateAndApiKey), AccessCaller.System);
        var before = await ReadDocumentAsync(machine.Id);

        await HashStore.StoreAsync(Organization, Project, machine.Id, "hash-2", [MachineScope.ConfigurationRead]);

        ChangedProperties(before!, (await ReadDocumentAsync(machine.Id))!)
            .ShouldBe([nameof(MachineAccessEntity.HashedSecret), nameof(MachineAccessEntity.Scopes)]);
        var entry = await HashStore.GetAsync(Organization, Project, machine.Id);
        entry.ShouldNotBeNull().HashedSecret.ShouldBe("hash-2");
        entry.Scopes.ShouldBe([MachineScope.ConfigurationRead]);
    }

    // The properties that differ between two reads of a document, without the system ones (_etag, _ts, …).
    private static string[] ChangedProperties(JsonObject before, JsonObject after)
    {
        return before.Select(property => property.Key)
            .Union(after.Select(property => property.Key))
            .Where(name => !name.StartsWith('_'))
            .Where(name => !JsonNode.DeepEquals(before[name], after[name]))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // The stored hash of a machine document.
    private static string Hash(JsonObject document)
    {
        return document[nameof(MachineAccessEntity.HashedSecret)]!.GetValue<string>();
    }

    // Moves with every write, so an equal one proves that nothing was written.
    private static string ETag(JsonObject document)
    {
        return document["_etag"]!.GetValue<string>();
    }

    private static string DeletedDuringItsReset(string id)
    {
        return $"The machine access {id} was deleted during its reset.";
    }

    // The step runs before the first patch only; a later patch (the retry) goes through as it is.
    private static Func<Task> FirstOnly(Func<Task> step)
    {
        var ran = false;
        return () =>
        {
            if (ran)
            {
                return Task.CompletedTask;
            }

            ran = true;
            return step();
        };
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
        };
    }

    // An own certificate that expires within the renewal window (30 days), so it can be renewed at once.
    private static MachineAccessCreate RenewableModel()
    {
        return Model(MachineCredentialKind.CertificateAndApiKey) with { CertificateLifetimeDays = 10 };
    }

    // The access audit records of the machine's resets, oldest first.
    private async Task<IReadOnlyList<StoredAccessAudit>> ResetRecordsAsync(string machineId)
    {
        return (await StoredAccessAudit.ReadMachineAsync(fixture.Services, Organization, Project, machineId))
            .Where(stored => stored.Record.Action == AccessAuditAction.MachineReset)
            .ToList();
    }

    private async Task ShouldKeepTheRenewalAsync(MachineAccess machine, string renewedThumbprint, MachineAccess reset)
    {
        var stored = (await ReadDocumentAsync(machine.Id))!;
        renewedThumbprint.ShouldNotBe(machine.CertificateThumbprint);
        stored[nameof(MachineAccessEntity.CertificateThumbprint)]!.GetValue<string>().ShouldBe(renewedThumbprint);
        stored[nameof(MachineAccessEntity.PreviousThumbprint)]!.GetValue<string>().ShouldBe(machine.CertificateThumbprint);

        // The response carries the stored state after the reset, the renewal included.
        reset.CertificateThumbprint.ShouldBe(renewedThumbprint);
        reset.LastRenewalAt.ShouldNotBeNull();

        (await Validator.ValidateAsync(reset.AccessKey)).ShouldNotBeNull().Scopes.ShouldBe(Granted);
        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();
    }

    // The document differs from the one before the reset only by the hash the delete cleared, so no key works.
    private async Task ShouldHoldOnlyTheClearedHashAsync(JsonObject before, MachineAccess machine, string? issuedKey)
    {
        var after = (await ReadDocumentAsync(machine.Id)).ShouldNotBeNull();
        ChangedProperties(before, after).ShouldBe([nameof(MachineAccessEntity.HashedSecret)]);
        after[nameof(MachineAccessEntity.HashedSecret)].ShouldBeNull();

        (await Validator.ValidateAsync(machine.AccessKey)).ShouldBeNull();

        if (issuedKey is not null)
        {
            (await Validator.ValidateAsync(issuedKey)).ShouldBeNull();
        }
    }

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
                ],
                CredentialKind = MachineCredentialKind.ApiKey,
            },
            AccessCaller.System);

        return accessService;
    }

    // The real router, with the API key service on the given hash store.
    private CosmosAccessService CreateAccessService(IApiKeyHashStore hashStore)
    {
        return CreateAccessService(new PolicyAwareMachineAccessService(
            new ApiKeyMachineAccessService(hashStore),
            fixture.Services.GetRequiredService<CertificateMachineAccessService>(),
            fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>(),
            fixture.Services.GetRequiredService<IOptions<MachineAuthenticationOptions>>(),
            NullLogger<PolicyAwareMachineAccessService>.Instance,
            fixture.Services.GetRequiredService<IIdentityProviderMachineAccessService>()));
    }

    // The Cosmos store over a container that runs a step before each patch, after the store's own read.
    private CosmosMergedApiKeyHashStore CreateHashStore(Func<Task> beforePatch, List<HttpStatusCode> patchStatuses)
    {
        return new CosmosMergedApiKeyHashStore(
            CreateInterleavingProvider(beforePatch, patchStatuses),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>());
    }

    // The organization's real container behind a strict wrapper: reads go through, a patch or a batch runs the step first
    // and records the status it got. Any other call (an upsert, a replace) fails the test.
    private IContainerRepositoryProvider CreateInterleavingProvider(Func<Task> beforePatch, List<HttpStatusCode> patchStatuses, Func<Task>? beforeRead = null)
    {
        var real = Container;
        var container = new Mock<Container>(MockBehavior.Strict);
        container
            .Setup(c => c.ReadItemStreamAsync(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string id, PartitionKey partitionKey, ItemRequestOptions options, CancellationToken cancellation) =>
            {
                if (beforeRead is not null)
                {
                    await beforeRead();
                }

                return await real.ReadItemStreamAsync(id, partitionKey, options, cancellation);
            });
        container
            .Setup(c => c.PatchItemStreamAsync(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> operations, PatchItemRequestOptions options, CancellationToken cancellation) =>
            {
                await beforePatch();
                var response = await real.PatchItemStreamAsync(id, partitionKey, operations, options, cancellation);
                patchStatuses.Add(response.StatusCode);
                return response;
            });
        container
            .Setup(c => c.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns((PartitionKey partitionKey) => new InterleavingBatch(real.CreateTransactionalBatch(partitionKey), beforePatch, patchStatuses));

        // a real scope over the wrapper (the class is sealed): the keys are the organization's, the calls reach the wrapper
        var repository = new OrganizationContainer(Organization, container.Object);
        return Mock.Of<IContainerRepositoryProvider>(p => p.GetOrganizationContainer(Organization) == repository);
    }

    // A write to another field, as a renewal stamps it: it only moves the ETag.
    private async Task TouchAsync(string id)
    {
        using var response = await Container.PatchItemStreamAsync(
            id,
            PartitionKey,
            [PatchOperation.Set($"/{nameof(MachineAccessEntity.LastRenewalAt)}", DateTimeOffset.UtcNow)]);
        response.EnsureSuccessStatusCode();
    }

    private CosmosAccessService CreateAccessService(IMachineAccessService router, IContainerRepositoryProvider? repositoryProvider = null)
    {
        return new CosmosAccessService(
            repositoryProvider ?? fixture.Services.GetRequiredService<IContainerRepositoryProvider>(),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            router);
    }

    // A real delete: the credential first, then the document.
    private async Task DeleteAsync(string id)
    {
        (await fixture.Services.GetRequiredService<IAccessService>().DeleteMachineAccessAsync(Organization, Project, id, AccessCaller.System)).ShouldBeTrue();
    }

    // A delete in progress: the first step of a real delete (the hash cleared), and the document read afterwards.
    private async Task<JsonObject> ClearTheHashAsync(string id)
    {
        (await HashStore.DeleteAsync(Organization, Project, id)).ShouldBeTrue();
        return (await ReadDocumentAsync(id)).ShouldNotBeNull();
    }

    // A real renewal, presenting the current certificate: it replaces the certificate fields under the ETag.
    private async Task<string> RenewAsync(MachineAccess machine)
    {
        var result = await fixture.Services.GetRequiredService<ICertificateRenewalService>()
            .RenewAsync(Organization, Project, machine.Id, machine.CertificateThumbprint!);

        result.Outcome.ShouldBe(CertificateRenewalOutcome.Success);
        return result.Thumbprint!;
    }

    private async Task<JsonObject?> ReadDocumentAsync(string id)
    {
        using var response = await Container.ReadItemStreamAsync(id, PartitionKey);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return (await JsonNode.ParseAsync(response.Content))!.AsObject();
    }

    // Runs a step before or after the real router, as a concurrent writer would.
    private sealed class InterleavingRouter(IMachineAccessService inner) : IMachineAccessService
    {
        public Func<Task>? BeforeReset { get; init; }

        public Func<Task>? AfterReset { get; init; }

        public string? IssuedKey { get; private set; }

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
        {
            return inner.CreateMachineAccessAsync(model);
        }

        public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
        {
            return inner.ResetMachineAccessAsync(objectId, organization, project);
        }

        public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
        {
            return inner.DeleteMachineAccessAsync(objectId, organization, project);
        }

        public async Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
        {
            if (BeforeReset is not null)
            {
                await BeforeReset();
            }

            IssuedKey = await inner.ResetMachineAccessAsync(existingAccess, organization, project);

            if (AfterReset is not null)
            {
                await AfterReset();
            }

            return IssuedKey;
        }

        public Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
        {
            return inner.DeleteMachineAccessAsync(existingAccess, organization, project);
        }
    }

    // A router whose reset gets no secret, as an identity provider that answers without one.
    private sealed class SecretlessRouter : IMachineAccessService
    {
        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotSupportedException();

        public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project) => Task.FromResult<string?>(null);

        public Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project) => Task.FromResult<string?>(null);

        public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project) => throw new NotSupportedException();
    }

    // A batch of the real container that runs the step before it executes and records the status it got: the patch of
    // a hash or a mask with its access audit record. Any other operation fails the test.
    private sealed class InterleavingBatch(TransactionalBatch inner, Func<Task> beforeExecute, List<HttpStatusCode> statuses) : TransactionalBatch
    {
        public override TransactionalBatch CreateItem<T>(T item, TransactionalBatchItemRequestOptions? requestOptions = null)
        {
            inner.CreateItem(item, requestOptions);
            return this;
        }

        public override TransactionalBatch PatchItem(string id, IReadOnlyList<PatchOperation> patchOperations, TransactionalBatchPatchItemRequestOptions? requestOptions = null)
        {
            inner.PatchItem(id, patchOperations, requestOptions);
            return this;
        }

        public override TransactionalBatch CreateItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override TransactionalBatch ReadItem(string id, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override TransactionalBatch UpsertItem<T>(T item, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override TransactionalBatch UpsertItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override TransactionalBatch ReplaceItem<T>(string id, T item, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override TransactionalBatch ReplaceItemStream(string id, Stream streamPayload, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override TransactionalBatch DeleteItem(string id, TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

        public override async Task<TransactionalBatchResponse> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await beforeExecute();
            var response = await inner.ExecuteAsync(cancellationToken);
            statuses.Add(response.StatusCode);
            return response;
        }

        public override async Task<TransactionalBatchResponse> ExecuteAsync(TransactionalBatchRequestOptions requestOptions, CancellationToken cancellationToken = default)
        {
            await beforeExecute();
            var response = await inner.ExecuteAsync(requestOptions, cancellationToken);
            statuses.Add(response.StatusCode);
            return response;
        }
    }

    // Runs a step after the API key service read the stored hash and before it replaces it.
    private sealed class InterleavingHashStore(IApiKeyHashStore inner) : IApiKeyHashStore
    {
        public Func<Task>? BeforeReplace { get; init; }

        public Task StoreAsync(string organization, string project, string machineAccessId, string hashedSecret, IReadOnlyList<MachineScope> scopes)
        {
            return inner.StoreAsync(organization, project, machineAccessId, hashedSecret, scopes);
        }

        public async Task<bool> ReplaceAsync(string organization, string project, string machineAccessId, string currentHashedSecret, string newHashedSecret)
        {
            if (BeforeReplace is not null)
            {
                await BeforeReplace();
            }

            return await inner.ReplaceAsync(organization, project, machineAccessId, currentHashedSecret, newHashedSecret);
        }

        public Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId)
        {
            return inner.GetAsync(organization, project, machineAccessId);
        }

        public Task<bool> DeleteAsync(string organization, string project, string machineAccessId)
        {
            return inner.DeleteAsync(organization, project, machineAccessId);
        }
    }
}
