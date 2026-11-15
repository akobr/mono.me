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
/// Tests per-project machine authentication policy storage and enforcement.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PolicyEnforcementTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "policyproj";

    private static readonly AccessCaller Admin = AccessCaller.Account("admin-1", "Ada Admin") with { CorrelationId = "invocation-policy" };

    [Fact]
    public async Task GetPolicy_NoneSet_ReturnsNull()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();

        var policy = await policyStore.GetAsync(Organization, "nonexistent-project");

        policy.ShouldBeNull();
    }

    [Fact]
    public async Task SetAndGetPolicy_Roundtrips()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync();

        var policy = new MachineAuthenticationPolicy
        {
            CredentialKind = MachineCredentialKind.CertificateAndApiKey,
            CertificateLifetimeDays = 180,
        };

        await policyStore.SetAsync(Organization, Project, policy, AccessCaller.System);
        var retrieved = await policyStore.GetAsync(Organization, Project);

        retrieved.ShouldNotBeNull();
        retrieved!.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        retrieved.CertificateLifetimeDays.ShouldBe(180);
    }

    [Fact]
    public async Task SetPolicy_UpdatesExisting()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync();

        await policyStore.SetAsync(
            Organization,
            Project,
            new MachineAuthenticationPolicy
            {
                CredentialKind = MachineCredentialKind.ApiKey,
            },
            AccessCaller.System);

        await policyStore.SetAsync(
            Organization,
            Project,
            new MachineAuthenticationPolicy
            {
                CredentialKind = MachineCredentialKind.Certificate,
                CertificateLifetimeDays = 90,
            },
            AccessCaller.System);

        var retrieved = await policyStore.GetAsync(Organization, Project);

        retrieved.ShouldNotBeNull();
        retrieved!.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        retrieved.CertificateLifetimeDays.ShouldBe(90);
    }

    [Fact]
    public async Task SetPolicy_WithEnabledKinds_IsReadBackFromCosmos()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync();

        await policyStore.SetAsync(
            Organization,
            Project,
            new MachineAuthenticationPolicy
            {
                EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey],
                CredentialKind = MachineCredentialKind.CertificateAndApiKey,
                CertificateLifetimeDays = 60,
            },
            AccessCaller.System);

        // A store with an empty cache reads the document itself.
        var uncached = new CosmosMachineAuthenticationPolicyStore(
            fixture.Services.GetRequiredService<IContainerRepositoryProvider>(),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));
        var retrieved = await uncached.GetAsync(Organization, Project);

        retrieved.ShouldNotBeNull();
        retrieved.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey]);
        retrieved.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        retrieved.CertificateLifetimeDays.ShouldBe(60);
    }

    // Each change of the policy is recorded in core "access", where the project's access point lives, with the policy
    // before and after; the same policy again changes nothing and records nothing.
    [Fact]
    public async Task SetPolicy_RecordsEachChange_ButNotAnUnchangedPolicy()
    {
        const string project = "policyaudit";
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync(project);
        var first = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey],
            CredentialKind = MachineCredentialKind.ApiKey,
        };

        var second = first with
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate],
            CertificateLifetimeDays = 90,
        };

        await policyStore.SetAsync(Organization, project, first, Admin);
        await policyStore.SetAsync(Organization, project, first with { }, AccessCaller.System);
        await policyStore.SetAsync(Organization, project, second, Admin);

        var records = await PolicyRecordsAsync(project);
        records.Count.ShouldBe(2);

        foreach (var stored in records)
        {
            stored.ShouldBeBy(Admin, Organization, project, "access");
            stored.Record.Target.ShouldBeNull();
            stored.ShouldHoldNoSecret();
        }

        // Before the first, the project had no policy of its own: the empty one.
        records[0].Record.OldValue.ShouldBe("{}");
        var set = StoredAccessAudit.Json(records[0].Record.NewValue);
        set["EnabledCredentialKinds"]!.AsArray().Select(kind => kind!.GetValue<string>()).ShouldBe([nameof(MachineCredentialKind.ApiKey)]);
        set["CredentialKind"]!.GetValue<string>().ShouldBe(nameof(MachineCredentialKind.ApiKey));
        records[1].Record.OldValue.ShouldBe(records[0].Record.NewValue);
        var changed = StoredAccessAudit.Json(records[1].Record.NewValue);
        changed["EnabledCredentialKinds"]!.AsArray().Select(kind => kind!.GetValue<string>())
            .ShouldBe([nameof(MachineCredentialKind.ApiKey), nameof(MachineCredentialKind.Certificate)]);
        changed["CertificateLifetimeDays"]!.GetValue<int>().ShouldBe(90);
    }

    // The policy is replaced with the ETag of its read: a change of the access point between the read and the replace
    // (here its description) fails the replace and its record together (412), and the retry keeps both changes.
    [Fact]
    public async Task SetPolicy_KeepsAChangeOfTheAccessPointBetweenItsReadAndItsReplace()
    {
        const string project = "policyrace";
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        await EnsureAccessPointAsync(project);
        var reads = 0;
        var store = new CosmosMachineAuthenticationPolicyStore(
            CreateCoreInterleavingProvider(async () =>
            {
                if (++reads == 1)
                {
                    await accessService.UpdateAccessPointAsync($"{Organization}.{project}", new AccessPointUpdate { Description = "Changed meanwhile" }, AccessCaller.System);
                }
            }),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));

        await store.SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate }, Admin);

        reads.ShouldBe(2);
        var point = (await accessService.GetAccessPointAsync($"{Organization}.{project}")).ShouldNotBeNull();
        point.Description.ShouldBe("Changed meanwhile");
        var stored = await new CosmosMachineAuthenticationPolicyStore(
            fixture.Services.GetRequiredService<IContainerRepositoryProvider>(),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions())).GetAsync(Organization, project);
        stored.ShouldNotBeNull().CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        (await PolicyRecordsAsync(project)).ShouldHaveSingleItem().Record.Actor.ShouldBe(Admin.Actor);
    }

    // The machine authentication policy records of the project in core "access", oldest first.
    private async Task<IReadOnlyList<StoredAccessAudit>> PolicyRecordsAsync(string project)
    {
        return (await StoredAccessAudit.ReadCoreAsync(fixture.Services, $"{Organization}.{project}"))
            .Where(stored => stored.Record.Action == AccessAuditAction.MachineAuthenticationPolicyChanged)
            .ToList();
    }

    // The real core container behind a wrapper that runs the step after each read, as a concurrent writer would land
    // between the read and the write; the writes go through.
    private IContainerRepositoryProvider CreateCoreInterleavingProvider(Func<Task> afterRead)
    {
        var real = fixture.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
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

        return Mock.Of<IContainerRepositoryProvider>(p => p.GetCore() == new ContainerRepository(container.Object));
    }

    private async Task EnsureAccessPointAsync(string project = Project)
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
    }
}
