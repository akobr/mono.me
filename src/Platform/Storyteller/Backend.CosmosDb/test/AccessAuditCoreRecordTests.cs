#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The access audit records of core "access": memberships, access points, both policies and the base seeding. A change
// writes exactly one record in its batch, with the caller (and the actor it came through), the target, the old and the
// new value and the correlation id; a change that changes nothing writes none, and a failed batch stores neither.
public class AccessAuditCoreRecordTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string OwnerId = "aud-core-owner";
    private const string MemberName = "Bob";

    // One organization per run of the class, so a reused emulator never mixes runs; every test has a project of its own.
    private static readonly string Organization = $"aud-core-{Guid.NewGuid().ToString("N")[..8]}";

    // The owner acting through the MCP server, as GetAccessCaller builds it from a Storyteller user token.
    private static readonly AccessCaller ViaMcp = new(AuditActor.Account(OwnerId, "Ada") with { Via = "mcp" }) { CorrelationId = "corr-1" };

    private static readonly AccessCaller Owner = AccessCaller.Account(OwnerId, "Owner");

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    private IContainerRepositoryProvider Repositories => Context.Services.GetRequiredService<IContainerRepositoryProvider>();

    private IOptions<JsonSerializerOptions> SerializerOptions => Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>();

    [Fact]
    public async Task Grant_NewMember_RecordsTheRoleWithTheCallerAndTheMemberName()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var added = await AddedAsync(pointKey, () => Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Contributor), ViaMcp));

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberGranted, pointKey, ViaMcp)
        {
            Target = AccessAuditTarget.Account(memberId, MemberName),
            NewValue = "Contributor",
        });
        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.Contributor);
    }

    [Fact]
    public async Task Grant_ByTheSystem_IsRefused_AndRecordsNothing()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var added = await AddedAsync(pointKey, async () =>
        {
            // the account of the caller is checked, not the CreatedById of the body: System has no role anywhere
            var grant = () => Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader) with { CreatedById = OwnerId }, AccessCaller.System);
            await grant.Should().ThrowAsync<AccessDeniedException>();
        });

        added.Should().BeEmpty();
        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.None);
    }

    [Fact]
    public async Task Grant_RaisingARole_RecordsTheOldAndTheNewRole()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader), Owner);

        var added = await AddedAsync(pointKey, () => Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Administrator), Owner));

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberGranted, pointKey, Owner)
        {
            Target = AccessAuditTarget.Account(memberId, MemberName),
            OldValue = "Reader",
            NewValue = "Administrator",
        });
    }

    [Fact]
    public async Task Grant_SameOrLowerRole_RecordsNothing()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Administrator), Owner);

        var added = await AddedAsync(pointKey, async () =>
        {
            (await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Administrator), Owner)).Should().BeFalse();
            (await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader), Owner)).Should().BeFalse();
        });

        added.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_RecordsTheRoleTaken_AndARoleTheAccountDoesNotHaveRecordsNothing()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        var outsiderId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Contributor), Owner);

        var added = await AddedAsync(pointKey, async () =>
        {
            (await Access.RevokePermissionAsync(Permission(memberId, pointKey, AccountRole.Contributor), ViaMcp)).Should().BeTrue();
            (await Access.RevokePermissionAsync(Permission(outsiderId, pointKey, AccountRole.Reader), ViaMcp)).Should().BeFalse();
            (await Access.RevokePermissionAsync(Permission(memberId, pointKey, AccountRole.Contributor), ViaMcp)).Should().BeFalse();
        });

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberRevoked, pointKey, ViaMcp)
        {
            Target = AccessAuditTarget.Account(memberId, MemberName),
            OldValue = "Contributor",
        });
    }

    [Fact]
    public async Task SetMemberRole_RecordsTheChange_AndTheSameRoleRecordsNothing()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Administrator), Owner);

        var added = await AddedAsync(pointKey, async () =>
        {
            await Access.SetMemberRoleAsync(pointKey, memberId, AccountRole.Reader, ViaMcp);
            await Access.SetMemberRoleAsync(pointKey, memberId, AccountRole.Reader, ViaMcp);
        });

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberRoleChanged, pointKey, ViaMcp)
        {
            Target = AccessAuditTarget.Account(memberId, MemberName),
            OldValue = "Administrator",
            NewValue = "Reader",
        });
    }

    [Fact]
    public async Task RemoveMember_RecordsTheRemovedRoleAndTheMemberName()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.ContributorWithSecrets), Owner);

        var added = await AddedAsync(pointKey, () => Access.RemoveMemberAsync(pointKey, memberId, ViaMcp));

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberRemoved, pointKey, ViaMcp)
        {
            Target = AccessAuditTarget.Account(memberId, MemberName),
            OldValue = "ContributorWithSecrets",
        });
    }

    [Fact]
    public async Task RemoveMember_Leaving_RecordsTheMemberAsActorAndTarget()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        var member = AccessCaller.Account(memberId, MemberName) with { CorrelationId = "corr-leave" };
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader), Owner);

        var added = await AddedAsync(pointKey, () => Access.RemoveMemberAsync(pointKey, memberId, member));

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberRemoved, pointKey, member)
        {
            Target = AccessAuditTarget.Account(memberId, MemberName),
            OldValue = "Reader",
        });
    }

    [Fact]
    public async Task AcceptInvitation_RecordsTheJoinByTheInviteeWithTheInvitationAndItsSender()
    {
        var pointKey = await CreateProjectAsync();
        var invitations = Context.Services.GetRequiredService<IInvitationService>();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var invitee = new InvitationIdentity($"user_{Guid.NewGuid():N}", email, IsEmailVerified: true, UserName: email, Name: "Invitee");
        var invitation = await invitations.CreateInvitationAsync(pointKey, new InvitationCreate { Email = email, Role = AccountRole.Contributor }, OwnerId, "Owner");

        var added = await AddedAsync(pointKey, async () =>
        {
            await invitations.AcceptInvitationAsync(invitation.Id, invitee, "corr-join");

            // accepted again by a member with the role: nothing changes, nothing is recorded
            await invitations.AcceptInvitationAsync(invitation.Id, invitee, "corr-again");
        });

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(
            AccessAuditAction.MemberJoined,
            pointKey,
            AccessCaller.Account(invitee.AccountId, "Invitee") with { CorrelationId = "corr-join" })
        {
            Target = AccessAuditTarget.Account(invitee.AccountId, "Invitee"),
            NewValue = "Contributor",
            Detail = $"Invitation {invitation.Id} sent by Owner ({OwnerId}).",
        });
        (await Access.GetAccountRoleAsync(invitee.AccountId, pointKey)).Should().Be(AccountRole.Contributor);
    }

    [Fact]
    public async Task CreateAccessPoint_InAnExistingOrganization_RecordsOnlyTheProject()
    {
        await EnsureOwnerAsync();
        var pointKey = $"{Organization}.{NewName("p")}";
        var ofOrganization = await ReadCoreAsync(Organization);

        await Access.CreateAccessPointAsync(
            new AccessPointCreate { Organization = Organization, Project = ProjectOf(pointKey), ProjectDescription = "  Billing.  ", OwnerId = OwnerId },
            ViaMcp);

        (await ReadCoreAsync(pointKey)).Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointCreated, pointKey, ViaMcp)
        {
            Target = AccessAuditTarget.Account(OwnerId, "Owner"),
            NewValue = "Owner",
            Detail = "Billing.",
        });
        (await ReadCoreAsync(Organization)).Should().BeEquivalentTo(ofOrganization, "the organization existed");
    }

    [Fact]
    public async Task CreateAccessPoint_OfANewOrganization_RecordsTheOrganizationAndTheProject()
    {
        await EnsureOwnerAsync();
        var organization = NewName("o");
        var pointKey = $"{organization}.{NewName("p")}";

        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = ProjectOf(pointKey), OwnerId = OwnerId }, Owner);

        (await ReadCoreAsync(organization)).Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointCreated, organization, Owner)
        {
            Target = AccessAuditTarget.Account(OwnerId, "Owner"),
            NewValue = "Owner",
        });
        (await ReadCoreAsync(pointKey)).Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointCreated, pointKey, Owner)
        {
            Target = AccessAuditTarget.Account(OwnerId, "Owner"),
            NewValue = "Owner",
        });
    }

    [Fact]
    public async Task CreateAccount_WithAnOrganizationAndAProject_StoresTheAccountWithTheRecordsOfBoth()
    {
        var accountId = $"acc-{Guid.NewGuid():N}";
        var organization = NewName("o");
        var pointKey = $"{organization}.{NewName("p")}";
        var founder = AccessCaller.Account(accountId, "Founder") with { CorrelationId = "corr-register" };

        await Access.CreateAccountAsync(
            new AccountCreate
            {
                IdentityId = accountId,
                UserName = $"{accountId}@example.com",
                Name = "Founder",
                Organization = organization,
                Project = ProjectOf(pointKey),
                ProjectDescription = "Shipping.",
            },
            founder);

        (await Access.GetAccountAsync(accountId))!.AccessMap.Should().Contain(organization, AccountRole.Owner).And.Contain(pointKey, AccountRole.Owner);
        (await ReadCoreAsync(organization)).Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointCreated, organization, founder)
        {
            Target = AccessAuditTarget.Account(accountId, "Founder"),
            NewValue = "Owner",
        });
        (await ReadCoreAsync(pointKey)).Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointCreated, pointKey, founder)
        {
            Target = AccessAuditTarget.Account(accountId, "Founder"),
            NewValue = "Owner",
            Detail = "Shipping.",
        });
    }

    [Fact]
    public async Task UpdateAccessPoint_RecordsTheOldAndTheNewDescription_AndAnUnchangedOneRecordsNothing()
    {
        var pointKey = await CreateProjectAsync("First.");

        var added = await AddedAsync(pointKey, async () =>
        {
            await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Second." }, ViaMcp);
            await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "  Second. " }, ViaMcp);
            await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate(), Owner);
        });

        added.Should().HaveCount(2);
        added[0].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointUpdated, pointKey, ViaMcp) { OldValue = "First.", NewValue = "Second." });
        added[1].ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointUpdated, pointKey, Owner) { OldValue = "Second." });
    }

    [Fact]
    public async Task KeyValuePolicy_AChangedAccessView_IsRecorded_KeyVersionsAndRewrapAreNot()
    {
        var pointKey = await CreateProjectAsync();
        var project = ProjectOf(pointKey);
        var store = Context.Services.GetRequiredService<IKeyValuePolicyStore>();
        var changed = new KeyValuePolicy { RequireMtlsForSecretDelivery = false, Version = 1 };

        var added = await AddedAsync(pointKey, async () =>
        {
            await store.SetAsync(Organization, project, changed, ViaMcp);

            // what the maintenance runs, a verification and a rotation write: no view of who gets secrets changes
            var keyInfo = changed with
            {
                Standard = new ProtectionKeyInfo { KeyId = "kek-standard", CurrentKeyVersion = "v2", CheckedAt = DateTimeOffset.UtcNow },
                Rewrap = new RewrapStatus { Protection = KeyValueProtection.Standard, TargetKeyVersion = "v2", StartedAt = DateTimeOffset.UtcNow, Remaining = 3 },
                Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UtcNow),
                Version = 2,
            };
            await store.SetAsync(Organization, project, keyInfo, AccessCaller.System);
        });

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.KeyValuePolicyChanged, pointKey, ViaMcp)
        {
            OldValue = AccessAuditValues.KeyValuePolicy(KeyValuePolicyDefaults.Create()),
            NewValue = AccessAuditValues.KeyValuePolicy(changed),
        });
        added[0].NewValue.Should().Contain("\"RequireMtlsForSecretDelivery\":false");
    }

    [Fact]
    public async Task KeyValuePolicy_TheAllowedLevelsInAnotherOrder_RecordNothing()
    {
        var pointKey = await CreateProjectAsync();
        var store = Context.Services.GetRequiredService<IKeyValuePolicyStore>();

        var added = await AddedAsync(pointKey, () => store.SetAsync(
            Organization,
            ProjectOf(pointKey),
            new KeyValuePolicy { AllowedProtections = [KeyValueProtection.Standard, KeyValueProtection.Public], Version = 1 },
            ViaMcp));

        added.Should().BeEmpty("the defaults allow Public and Standard, in the other order");
    }

    [Fact]
    public async Task KeyValuePolicy_ThroughThePolicyService_RecordsTheKeyValueCaller()
    {
        var pointKey = await CreateProjectAsync();
        var actor = AuditActor.Account("kv-admin", "Admin") with { Via = "mcp" };
        var caller = KeyValueCaller.WithSecrets(actor) with { CorrelationId = "corr-kv" };

        var added = await AddedAsync(pointKey, () => Context.Services.GetRequiredService<IKeyValuePolicyService>()
            .UpdateAsync(Organization, ProjectOf(pointKey), new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false }, caller));

        var record = added.Should().ContainSingle().Subject;
        record.Action.Should().Be(AccessAuditAction.KeyValuePolicyChanged);
        record.Actor.Should().Be(actor);
        record.CorrelationId.Should().Be("corr-kv");
    }

    [Fact]
    public async Task MachineAuthenticationPolicy_RecordsAChange_AndAnEqualPolicyRecordsNothing()
    {
        var pointKey = await CreateProjectAsync();
        var store = MachinePolicies(Repositories);
        var policy = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate],
            CredentialKind = MachineCredentialKind.Certificate,
            CertificateLifetimeDays = 30,
        };

        var added = await AddedAsync(pointKey, async () =>
        {
            await store.SetAsync(Organization, ProjectOf(pointKey), policy, ViaMcp);
            await store.SetAsync(Organization, ProjectOf(pointKey), policy with { }, ViaMcp);

            // the same kinds in another order, and twice, with the same default: no change
            await store.SetAsync(
                Organization,
                ProjectOf(pointKey),
                policy with { EnabledCredentialKinds = [MachineCredentialKind.Certificate, MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate] },
                ViaMcp);
        });

        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MachineAuthenticationPolicyChanged, pointKey, ViaMcp)
        {
            OldValue = AccessAuditValues.MachineAuthentication(null),
            NewValue = AccessAuditValues.MachineAuthentication(policy),
        });
        added[0].NewValue.Should().Be("{\"EnabledCredentialKinds\":[\"ApiKey\",\"Certificate\"],\"CredentialKind\":\"Certificate\",\"CertificateLifetimeDays\":30}");
    }

    [Fact]
    public async Task MachineAuthenticationPolicy_AMembershipChangedMeanwhile_IsKept()
    {
        // The policy used to be upserted over the access point it read, so a grant in between was lost.
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        var storage = new InterceptedRepositoryProvider(Repositories);
        var policy = new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate };
        var replaces = 0;
        storage.CoreInterceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, $"apt.{pointKey}") && ++replaces == 1)
            {
                await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader), Owner);
            }

            return null;
        };

        await MachinePolicies(storage).SetAsync(Organization, ProjectOf(pointKey), policy, Owner);

        storage.CoreInterceptor.Batches.Select(batch => batch.StatusCode).Should().Equal(HttpStatusCode.PreconditionFailed, HttpStatusCode.OK);
        var stored = await ReadPointAsync(pointKey);
        stored.AccessMap.Should().Contain(memberId, AccountRole.Reader);
        stored.MachineAuthentication.Should().Be(policy);
        (await ReadCoreAsync(pointKey)).Select(record => record.Action).Should().Equal(
            AccessAuditAction.AccessPointCreated,
            AccessAuditAction.MemberGranted,
            AccessAuditAction.MachineAuthenticationPolicyChanged);
    }

    [Fact]
    public async Task AFailedMembershipBatch_StoresNeitherTheChangeNorItsRecord()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        var storage = new InterceptedRepositoryProvider(Repositories);
        storage.CoreInterceptor.BeforeBatch = _ => Task.FromResult<HttpStatusCode?>(HttpStatusCode.ServiceUnavailable);
        var access = new CosmosAccessService(storage, SerializerOptions);

        var added = await AddedAsync(pointKey, async () =>
        {
            var grant = () => access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Contributor), Owner);
            (await grant.Should().ThrowAsync<CosmosException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        });

        added.Should().BeEmpty();
        storage.CoreInterceptor.Batches.Should().ContainSingle().Which.Operations.Select(operation => operation.Kind)
            .Should().Equal(BatchOperationKind.Replace, BatchOperationKind.Replace, BatchOperationKind.Create);
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().NotContainKey(memberId);
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedAccessPointBatch_StoresNoAccessPointAndNoRecord()
    {
        await EnsureOwnerAsync();
        var organization = NewName("o");
        var pointKey = $"{organization}.{NewName("p")}";
        var storage = new InterceptedRepositoryProvider(Repositories);
        storage.CoreInterceptor.BeforeBatch = _ => Task.FromResult<HttpStatusCode?>(HttpStatusCode.ServiceUnavailable);
        var access = new CosmosAccessService(storage, SerializerOptions);

        var create = () => access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = ProjectOf(pointKey), OwnerId = OwnerId }, Owner);

        (await create.Should().ThrowAsync<CosmosException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        storage.CoreInterceptor.Batches.Should().ContainSingle().Which.Operations.Should().HaveCount(5, "both access points, their records and the owner's account");
        (await Access.GetAccessPointAsync(organization)).Should().BeNull();
        (await Access.GetAccessPointAsync(pointKey)).Should().BeNull();
        (await Access.GetAccountAsync(OwnerId))!.AccessMap.Should().NotContainKey(organization).And.NotContainKey(pointKey);
        (await ReadCoreAsync(organization)).Should().BeEmpty();
        (await ReadCoreAsync(pointKey)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAccessPoint_TheOwnerDemotedBeforeTheBatch_IsRefused_AndTheDemotionStays()
    {
        var ownerId = $"acc-{Guid.NewGuid():N}";
        var otherOwnerId = await CreateBareAccountAsync();
        var organization = NewName("o");
        var demoter = AccessCaller.Account(otherOwnerId);
        await Access.CreateAccountAsync(
            new AccountCreate { IdentityId = ownerId, UserName = $"{ownerId}@example.com", Name = "Owner", Organization = organization, Project = NewName("p") },
            AccessCaller.Account(ownerId));
        await Access.GrantPermissionAsync(Permission(otherOwnerId, organization, AccountRole.Owner), AccessCaller.Account(ownerId));
        var pointKey = $"{organization}.{NewName("q")}";
        var storage = new InterceptedRepositoryProvider(Repositories);
        var access = new CosmosAccessService(storage, SerializerOptions);
        var demoted = false;

        // the other Owner demotes the creator between the ownership check and the batch of the creation
        storage.CoreInterceptor.BeforeBatch = async batch =>
        {
            if (!demoted && batch.Contains(BatchOperationKind.Create, $"{AccessPointEntity.IdPrefix}{pointKey}"))
            {
                demoted = true;
                await Access.SetMemberRoleAsync(organization, ownerId, AccountRole.Administrator, demoter);
            }

            return null;
        };

        var create = () => access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = ProjectOf(pointKey), OwnerId = ownerId }, AccessCaller.Account(ownerId));

        await create.Should().ThrowAsync<AccessDeniedException>("the check runs again on the organization that refused the batch");
        storage.CoreInterceptor.Batches.Select(batch => batch.StatusCode).Should().Equal(HttpStatusCode.PreconditionFailed);
        (await Access.GetAccessPointAsync(pointKey)).Should().BeNull();
        (await Access.GetAccountAsync(ownerId))!.AccessMap.Should().Contain(organization, AccountRole.Administrator, "a stale read never writes Owner back");
        (await ReadCoreAsync(pointKey)).Should().BeEmpty();
    }

    [Fact]
    public async Task Seeding_AnAccessPointChangedBetweenItsReadAndTheBatch_ReadsBothAgain()
    {
        var organization = NewName("o");
        var pointKey = $"{organization}.{NewName("p")}";
        var accountId = $"acc-{Guid.NewGuid():N}";
        var core = Repositories.GetCore().Container;
        var partitionKey = new PartitionKey(AccessPointEntity.Partition);
        await Data.SeedAccountAccessAsync(core, accountId, "seed@example.com", "Seed", new Dictionary<string, AccountRole> { [pointKey] = AccountRole.Owner });

        // the account lost the role, so the next seed writes the account and only guards the access point
        var account = (await core.ReadItemAsync<AccountEntity>(accountId, partitionKey)).Resource;
        await core.ReplaceItemAsync(account with { AccessMap = [] }, accountId, partitionKey);
        var storage = new InterceptedRepositoryProvider(Repositories);
        var changed = false;

        // someone changes the access point after the seed read it and before its batch
        storage.CoreInterceptor.BeforeBatch = async _ =>
        {
            if (!changed)
            {
                changed = true;
                var point = (await core.ReadItemAsync<AccessPointEntity>($"{AccessPointEntity.IdPrefix}{pointKey}", partitionKey)).Resource;
                await core.ReplaceItemAsync(point with { AccessMap = new() { [accountId] = AccountRole.Reader } }, point.Id, partitionKey);
            }

            return null;
        };

        var added = await AddedAsync(pointKey, () =>
            Data.SeedAccountAccessAsync(storage.GetCore().Container, accountId, "seed@example.com", "Seed", new Dictionary<string, AccountRole> { [pointKey] = AccountRole.Owner }));

        storage.CoreInterceptor.Batches.Select(batch => batch.StatusCode).Should().Equal(HttpStatusCode.PreconditionFailed, HttpStatusCode.OK);
        added.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberRoleChanged, pointKey, AccessCaller.System)
        {
            Target = AccessAuditTarget.Account(accountId, "Seed"),
            OldValue = "Reader",
            NewValue = "Owner",
            Detail = "Seeded by DbCreator.",
        });
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().Contain(accountId, AccountRole.Owner);
        (await Access.GetAccountAsync(accountId))!.AccessMap.Should().Contain(pointKey, AccountRole.Owner);
    }

    [Fact]
    public async Task Seeding_RecordsEveryAccessPointItCreatesOrChanges_BySystem()
    {
        var organization = NewName("o");
        var pointKey = $"{organization}.{NewName("p")}";
        var accountId = $"acc-{Guid.NewGuid():N}";
        var core = Repositories.GetCore().Container;
        var system = AccessCaller.System;

        await Data.SeedAccountAccessAsync(core, accountId, "seed@example.com", "Seed", Roles(organization, pointKey, AccountRole.Owner));
        var again = await AddedAsync(pointKey, () => Data.SeedAccountAccessAsync(core, accountId, "seed@example.com", "Seed", Roles(organization, pointKey, AccountRole.Owner)));
        var lowered = await AddedAsync(pointKey, () => Data.SeedAccountAccessAsync(core, accountId, "seed@example.com", "Seed", new Dictionary<string, AccountRole> { [pointKey] = AccountRole.Administrator }));

        (await ReadCoreAsync(organization)).Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.AccessPointCreated, organization, system)
        {
            Target = AccessAuditTarget.Account(accountId, "Seed"),
            NewValue = "Owner",
            Detail = "Seeded by DbCreator.",
        });
        again.Should().BeEmpty("an unchanged role writes nothing");
        lowered.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberRoleChanged, pointKey, system)
        {
            Target = AccessAuditTarget.Account(accountId, "Seed"),
            OldValue = "Owner",
            NewValue = "Administrator",
            Detail = "Seeded by DbCreator.",
        });
        (await ReadCoreAsync(pointKey)).Select(record => record.Action).Should().Equal(AccessAuditAction.AccessPointCreated, AccessAuditAction.MemberRoleChanged);
    }

    [Fact]
    public async Task Seeding_WritesTheAccountInTheBatchOfItsRecord_AndRepairsARoleOnlyTheAccessPointHolds()
    {
        var organization = NewName("o");
        var pointKey = $"{organization}.{NewName("p")}";
        var accountId = $"acc-{Guid.NewGuid():N}";
        var core = Repositories.GetCore().Container;
        var partitionKey = new PartitionKey(AccessPointEntity.Partition);
        var storage = new InterceptedRepositoryProvider(Repositories);

        await Data.SeedAccountAccessAsync(storage.GetCore().Container, accountId, "seed@example.com", "Seed", Roles(organization, pointKey, AccountRole.Owner));

        // every membership is one batch: the access point, the account and the record
        storage.CoreInterceptor.Batches.Should().HaveCount(2).And.OnlyContain(batch => batch.Contains(BatchOperationKind.Create, accountId) || batch.Contains(BatchOperationKind.Replace, accountId));
        (await core.ReadItemAsync<AccountEntity>(accountId, partitionKey)).Resource.AccessMap.Should().Contain(pointKey, AccountRole.Owner);

        // the account lost the role behind the seed's back (the access point still has it): the role checks read the
        // account, so the next seed repairs it and records the grant
        var account = (await core.ReadItemAsync<AccountEntity>(accountId, partitionKey)).Resource;
        await core.ReplaceItemAsync(account with { AccessMap = new Dictionary<string, AccountRole> { [organization] = AccountRole.Owner } }, accountId, partitionKey);

        var repaired = await AddedAsync(pointKey, () => Data.SeedAccountAccessAsync(core, accountId, "seed@example.com", "Seed", Roles(organization, pointKey, AccountRole.Owner)));

        repaired.Should().ContainSingle().Which.ShouldMatch(new ExpectedAccessAudit(AccessAuditAction.MemberGranted, pointKey, AccessCaller.System)
        {
            Target = AccessAuditTarget.Account(accountId, "Seed"),
            NewValue = "Owner",
            Detail = "Repaired by DbCreator: the account did not hold the role of the access point.",
        });
        (await core.ReadItemAsync<AccountEntity>(accountId, partitionKey)).Resource.AccessMap.Should().Contain(pointKey, AccountRole.Owner);
    }

    [Fact]
    public async Task Core_HasTimeToLiveOnWithoutADefaultExpiry()
    {
        var core = (await Repositories.GetCore().Container.ReadContainerAsync()).Resource;

        core.DefaultTimeToLive.Should().Be(-1, "only the records have a ttl in core");
        core.PartitionKeyPath.Should().Be("/PartitionKey");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3600)]
    public async Task CreateContainerIfNotExists_AContainerCreatedBefore_GetsTimeToLiveWithoutADefaultExpiry(int? stored)
    {
        // A container with the key of core, as one created before the records; a name of its own, deleted afterwards.
        var database = Context.Services.GetRequiredService<ICosmosClientProvider>().Client.GetDatabase(CosmosConstants.DatabaseName);
        var name = $"bare-core-{Guid.NewGuid():N}";
        var bare = (await database.CreateContainerAsync(new ContainerProperties(name, "/PartitionKey") { DefaultTimeToLive = stored })).Container;

        try
        {
            (await bare.ReadContainerAsync()).Resource.DefaultTimeToLive.Should().Be(stored);

            await Context.Services.GetRequiredService<IContainerFactory>().CreateContainerIfNotExistsAsync(name);

            var settled = (await bare.ReadContainerAsync()).Resource;
            settled.DefaultTimeToLive.Should().Be(-1);
            settled.PartitionKeyPath.Should().Be("/PartitionKey");
        }
        finally
        {
            await bare.DeleteContainerAsync();
        }
    }

    // CreatedById is not the caller: the service authorizes the caller and ignores it.
    private static Permission Permission(string accountId, string pointKey, AccountRole role) => new()
    {
        CreatedById = "not-the-caller",
        AccountId = accountId,
        AccessPointKey = pointKey,
        Role = role,
    };

    private static Dictionary<string, AccountRole> Roles(string organization, string pointKey, AccountRole role) => new()
    {
        [organization] = role,
        [pointKey] = role,
    };

    private static string NewName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private static string ProjectOf(string pointKey) => pointKey.Split('.', 2)[1];

    private CosmosMachineAuthenticationPolicyStore MachinePolicies(IContainerRepositoryProvider repositories) => new(
        repositories,
        SerializerOptions,
        new MemoryCache(new MemoryCacheOptions()));

    private Task<IReadOnlyList<AccessAuditEntity>> ReadCoreAsync(string accessPointKey)
    {
        return StoredAccessAudit.ReadCoreAsync(Repositories, accessPointKey);
    }

    private Task<IReadOnlyList<AccessAuditEntity>> AddedAsync(string accessPointKey, Func<Task> change)
    {
        return StoredAccessAudit.AddedByAsync(() => ReadCoreAsync(accessPointKey), change);
    }

    private async Task EnsureOwnerAsync()
    {
        if (await Access.GetAccountAsync(OwnerId) is not null)
        {
            return;
        }

        await Access.CreateAccountAsync(
            new AccountCreate { IdentityId = OwnerId, UserName = "owner@example.com", Name = "Owner", Organization = Organization, Project = "base" },
            Owner);
    }

    private async Task<string> CreateProjectAsync(string? description = null)
    {
        await EnsureOwnerAsync();
        var point = await Access.CreateAccessPointAsync(
            new AccessPointCreate { Organization = Organization, Project = NewName("p"), ProjectDescription = description, OwnerId = OwnerId },
            Owner);
        return point.Key;
    }

    // An account without memberships, created directly: CreateAccountAsync with a project always creates an organization.
    private async Task<string> CreateBareAccountAsync()
    {
        var id = $"acc-{Guid.NewGuid():N}";
        await Repositories.GetCore().Container.CreateItemAsync(
            new AccountEntity { Id = id, UserName = $"{id}@example.com", Name = MemberName, AccessMap = new() },
            new PartitionKey(AccessPointEntity.Partition));
        return id;
    }

    private async Task<AccessPointEntity> ReadPointAsync(string pointKey)
    {
        return (await Repositories.GetCore().Container.TryReadItemAsync(
            $"apt.{pointKey}",
            new PartitionKey(AccessPointEntity.Partition),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(SerializerOptions.Value)))!;
    }
}
