using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// What DbCreator leaves in the access audit: a record by the System of every access point it creates and every
// membership it changes, in core "access" next to the change; nothing for a run that changes no access. And core has
// TTL on, so the records expire.
public class AccessAuditTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
    private const string Project = "ledger";

    // the default retention, two years
    private static readonly int RetentionInSeconds = (int)TimeSpan.FromDays(new AccessAuditOptions().RetentionDays).TotalSeconds;

    [Fact]
    public async Task Seed_ANewProject_RecordsItsAccessPointsByTheSystem_AndAnotherRunNothing()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());

        (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).HasFailures.ShouldBeFalse();

        // the organization and the project are new: a record each, with the owner as Owner
        var records = await StoredItems.ReadAccessAuditAsync(Services, organization);
        records.Select(record => (record.AccessPointKey, record.Project, record.Action)).ShouldBe(
            [
                (organization, (string?)null, AccessAuditAction.AccessPointCreated),
                ($"{organization}.{Project}", Project, AccessAuditAction.AccessPointCreated),
            ],
            ignoreOrder: true);

        foreach (var record in records)
        {
            (record.PartitionKey, record.Organization, record.TimeToLiveInSeconds).ShouldBe((AccessPointEntity.Partition, organization, RetentionInSeconds));
            record.Actor.ShouldBe(AuditActor.System);
            record.Target.ShouldBe(AccessAuditTarget.Account(owner, owner));
            (record.OldValue, record.NewValue, record.CorrelationId).ShouldBe(((string?)null, nameof(AccountRole.Owner), (string?)null));
        }

        records.Single(record => record.Project is null).Detail.ShouldBeNull();
        records.Single(record => record.Project == Project).Detail.ShouldBe("Seeded by the integration tests of DbCreator.");

        // the same seed again changes no access
        (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).Sets.Single().IsAlreadySeeded.ShouldBeTrue();
        (await StoredItems.ReadAccessAuditAsync(Services, organization)).Select(record => record.Id).ShouldBe(records.Select(record => record.Id));
    }

    [Fact]
    public async Task Seed_AnExistingProject_RecordsNothing()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());
        var caller = AccessCaller.Account(owner, "The Owner") with { CorrelationId = "invocation-1" };
        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = Project, OwnerId = owner }, caller);
        var before = await StoredItems.ReadAccessAuditAsync(Services, organization);

        var run = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        // the owner's account is created (an account without a project has no access to record), the project is used
        // as it is
        run.HasFailures.ShouldBeFalse();
        run.Provisioning.ShouldBe(new DataSetProvisioning(IsAccountCreated: true, IsOrganizationCreated: false, IsProjectCreated: false, IsViewRegistered: false));
        before.Count.ShouldBe(2);
        before.ShouldAllBe(record => record.Actor == caller.Actor && record.CorrelationId == caller.CorrelationId);
        (await StoredItems.ReadAccessAuditAsync(Services, organization)).Select(record => record.Id).ShouldBe(before.Select(record => record.Id));
    }

    // The fixture's build of a new database seeded the base owner's access.
    [Fact]
    public async Task Build_RecordsTheAccessPointsOfTheBaseOwnerByTheSystem_AndAnotherBuildNothing()
    {
        var baseOwner = Services.GetRequiredService<IOptions<BaseAccountOptions>>().Value.GetAccountId();
        var records = await StoredItems.ReadAccessAuditAsync(Services, Constants.ORGANIZATION);

        records.Select(record => record.AccessPointKey).ShouldBe(
            [Constants.ORGANIZATION, $"{Constants.ORGANIZATION}.{Storyteller.Constants.DefaultProjectName}"],
            ignoreOrder: true);

        foreach (var record in records)
        {
            (record.Action, record.Actor).ShouldBe((AccessAuditAction.AccessPointCreated, AuditActor.System));
            record.Target.ShouldBe(AccessAuditTarget.Account(baseOwner, Constants.BASE_ACCOUNT_NAME));
            (record.OldValue, record.NewValue, record.Detail).ShouldBe(((string?)null, nameof(AccountRole.Owner), "Seeded by DbCreator."));
            record.TimeToLiveInSeconds.ShouldBe(RetentionInSeconds);
        }

        (await Services.GetRequiredService<CoreDbStructureBuilder>().BuildAsync()).HasFailures.ShouldBeFalse();

        (await StoredItems.ReadAccessAuditAsync(Services, Constants.ORGANIZATION)).Select(record => record.Id).ShouldBe(records.Select(record => record.Id));
    }

    [Fact]
    public async Task SeedAccountAccess_RecordsWhatItCreatesAndChanges_AndNothingForAnUnchangedRole()
    {
        var core = Services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
        var audit = Services.GetRequiredService<AccessAuditRecords>();
        var (organization, owner, member) = (NewOrganization(), NewOwner(), NewOwner());
        var project = $"{organization}.main";

        Task SeedAsync(string account, string name, params (string Key, AccountRole Role)[] roles)
        {
            return Data.SeedAccountAccessAsync(core, account, account, name, roles.ToDictionary(role => role.Key, role => role.Role), audit);
        }

        await SeedAsync(owner, "The Owner", (organization, AccountRole.Owner), (project, AccountRole.Owner));
        await SeedAsync(member, "The Member", (project, AccountRole.Contributor));
        await SeedAsync(member, "The Member", (project, AccountRole.Administrator));
        await SeedAsync(member, "The Member", (project, AccountRole.Administrator));
        await SeedAsync(owner, "The Owner", (organization, AccountRole.Owner), (project, AccountRole.Owner));

        var records = await StoredItems.ReadAccessAuditAsync(Services, organization);
        records.Select(record => (record.AccessPointKey, record.Action, record.Target, record.OldValue, record.NewValue)).ShouldBe(
        [
            (organization, AccessAuditAction.AccessPointCreated, AccessAuditTarget.Account(owner, "The Owner"), (string?)null, "Owner"),
            (project, AccessAuditAction.AccessPointCreated, AccessAuditTarget.Account(owner, "The Owner"), null, "Owner"),
            (project, AccessAuditAction.MemberGranted, AccessAuditTarget.Account(member, "The Member"), null, "Contributor"),
            (project, AccessAuditAction.MemberRoleChanged, AccessAuditTarget.Account(member, "The Member"), "Contributor", "Administrator"),
        ]);
        records.ShouldAllBe(record => record.Actor == AuditActor.System && record.PartitionKey == AccessPointEntity.Partition && record.Detail == "Seeded by DbCreator.");

        (await Access.GetAccessPointAsync(project))!.AccessMap.ShouldBe(
            new Dictionary<string, AccountRole> { [owner] = AccountRole.Owner, [member] = AccountRole.Administrator },
            ignoreOrder: true);
    }

    [Fact]
    public async Task Build_LeavesCoreWithTimeToLiveOn_AndAnExistingContainerGetsIt()
    {
        var database = Services.GetRequiredService<ICosmosClientProvider>().Client.GetDatabase(CosmosConstants.DatabaseName);

        // on without a default expiry: only the access audit records have a ttl in core; the default indexing stays
        var core = (await database.GetContainer(CosmosConstants.CoreContainerName).ReadContainerAsync(cancellationToken: Cancellation)).Resource;
        core.DefaultTimeToLive.ShouldBe(-1);
        core.PartitionKeyPath.ShouldBe(OrganizationKeys.PartitionKeyPath);
        core.IndexingPolicy.ExcludedPaths.Select(path => path.Path).ShouldNotContain(ContainerFactory.AuditEntriesPath);

        // a container of the single key created before, as core was, gets TTL on the next start; a name of its own
        // (never "org.", which DbCreator refuses), deleted afterwards
        var bareName = $"bare-{Guid.NewGuid():N}";
        await database.CreateContainerAsync(new ContainerProperties(bareName, OrganizationKeys.PartitionKeyPath), cancellationToken: Cancellation);
        var bare = database.GetContainer(bareName);

        try
        {
            (await bare.ReadContainerAsync(cancellationToken: Cancellation)).Resource.DefaultTimeToLive.ShouldBeNull();

            await Services.GetRequiredService<IContainerFactory>().CreateContainerIfNotExistsAsync(bareName);

            (await bare.ReadContainerAsync(cancellationToken: Cancellation)).Resource.DefaultTimeToLive.ShouldBe(-1);
        }
        finally
        {
            await bare.DeleteContainerAsync(cancellationToken: Cancellation);
        }
    }

    private static DataSetSeedRequest Request(TestSetFolder set, string organization, string owner)
    {
        return new DataSetSeedRequest
        {
            Paths = [set.Path],
            Organization = organization,
            Project = Project,
            Owner = owner,
        };
    }
}
