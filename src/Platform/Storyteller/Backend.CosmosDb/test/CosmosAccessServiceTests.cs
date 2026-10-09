using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosAccessServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    // one organization for the whole class, every test works in its own project
    private const string Organization = "acc-tests";
    private const string OwnerId = "acc-tests-owner";

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    [Fact]
    public async Task Grant_ByOwner_StoresMembershipInPointAndAccount()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var granted = await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));

        granted.Should().BeTrue();
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().Contain(memberId, AccountRole.Contributor);
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().Contain(pointKey, AccountRole.Contributor);
        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.Contributor);
    }

    [Fact]
    public async Task Grant_SameOrLowerRole_ReturnsFalse()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Administrator));

        var sameRole = await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Administrator));
        var lowerRole = await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        sameRole.Should().BeFalse();
        lowerRole.Should().BeFalse();
        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.Administrator);
    }

    [Fact]
    public async Task Grant_ByNonMember_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var outsiderId = await CreateBareAccountAsync();
        var memberId = await CreateBareAccountAsync();

        var act = () => Access.GrantPermissionAsync(Permission(outsiderId, memberId, pointKey, AccountRole.Reader));

        await act.Should().ThrowAsync<AccessDeniedException>();
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().NotContainKey(memberId);
    }

    [Fact]
    public async Task Grant_OwnerByAdministrator_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var adminId = await CreateBareAccountAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, adminId, pointKey, AccountRole.Administrator));

        var act = () => Access.GrantPermissionAsync(Permission(adminId, memberId, pointKey, AccountRole.Owner));

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task Grant_ByAdministrator_GrantsLowerRoles()
    {
        var pointKey = await CreateProjectAsync();
        var adminId = await CreateBareAccountAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, adminId, pointKey, AccountRole.Administrator));

        var granted = await Access.GrantPermissionAsync(Permission(adminId, memberId, pointKey, AccountRole.ContributorWithSecrets));

        granted.Should().BeTrue();
        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.ContributorWithSecrets);
    }

    [Fact]
    public async Task Grant_UnknownTarget_ThrowsNotFound()
    {
        var pointKey = await CreateProjectAsync();

        var act = () => Access.GrantPermissionAsync(Permission(OwnerId, $"missing-{Guid.NewGuid():N}", pointKey, AccountRole.Reader));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Grant_NoneRole_ThrowsArgumentException()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var act = () => Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.None));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Revoke_ByOwner_RemovesMembershipFromPointAndAccount()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));

        var revoked = await Access.RevokePermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));

        revoked.Should().BeTrue();
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().NotContainKey(memberId);
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().NotContainKey(pointKey);
    }

    [Fact]
    public async Task Revoke_ByNonMember_ThrowsAccessDenied()
    {
        // Regression: the creator check was inverted, so a caller without any membership could revoke.
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        var outsiderId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));

        var act = () => Access.RevokePermissionAsync(Permission(outsiderId, memberId, pointKey, AccountRole.Contributor));

        await act.Should().ThrowAsync<AccessDeniedException>();
        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.Contributor);
    }

    [Fact]
    public async Task Revoke_ByContributor_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var contributorId = await CreateBareAccountAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, contributorId, pointKey, AccountRole.Contributor));
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        var act = () => Access.RevokePermissionAsync(Permission(contributorId, memberId, pointKey, AccountRole.Reader));

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task Revoke_RoleBelowTheStoredOne_ThrowsConflict()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Administrator));

        var act = () => Access.RevokePermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ElevatedRole);
    }

    [Fact]
    public async Task Revoke_NotAMember_ReturnsFalse()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var revoked = await Access.RevokePermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        revoked.Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_LastOwner_ThrowsConflict()
    {
        var pointKey = await CreateProjectAsync();

        var act = () => Access.RevokePermissionAsync(Permission(OwnerId, OwnerId, pointKey, AccountRole.Owner));

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.LastOwner);
        (await Access.GetAccountRoleAsync(OwnerId, pointKey)).Should().Be(AccountRole.Owner);
    }

    [Fact]
    public async Task Revoke_OneOfTwoOwners_Succeeds()
    {
        var pointKey = await CreateProjectAsync();
        var secondOwnerId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, secondOwnerId, pointKey, AccountRole.Owner));

        var revoked = await Access.RevokePermissionAsync(Permission(OwnerId, secondOwnerId, pointKey, AccountRole.Owner));

        revoked.Should().BeTrue();
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, AccountRole>(OwnerId, AccountRole.Owner));
    }

    [Fact]
    public async Task CreateAccessPoint_SecondProject_AddsItToTheOwnerAccount()
    {
        // Regression: the owner account was upserted as the API model, without the Cosmos id.
        var first = await CreateProjectAsync();
        var second = await CreateProjectAsync();

        var owner = await Access.GetAccountAsync(OwnerId);
        var points = await Access.GetAccessPointsAsync(OwnerId);

        owner!.AccessMap.Should().Contain(first, AccountRole.Owner).And.Contain(second, AccountRole.Owner);
        points.Select(point => point.Key).Should().Contain([first, second]);
    }

    [Fact]
    public async Task CreateAccessPoint_ExistingProject_ThrowsConflict()
    {
        var pointKey = await CreateProjectAsync();
        var project = pointKey[(Organization.Length + 1)..];

        var act = () => Access.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = project, OwnerId = OwnerId });

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AccessPointExists);
    }

    [Fact]
    public async Task CreateAccessPoint_NotAnOrganizationOwner_ThrowsAccessDenied()
    {
        await EnsureOwnerAsync();
        var outsiderId = await CreateBareAccountAsync();

        var act = () => Access.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = NewProjectName(), OwnerId = outsiderId });

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task CreateAccount_ExistingAccount_ThrowsConflict()
    {
        await EnsureOwnerAsync();

        var act = () => Access.CreateAccountAsync(new AccountCreate
        {
            IdentityId = OwnerId,
            UserName = "owner@example.com",
            Name = "Owner",
            Organization = Organization,
            Project = NewProjectName(),
        });

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AccountExists);
    }

    [Fact]
    public async Task GetAccessPoints_UnknownAccount_ThrowsNotFound()
    {
        var act = () => Access.GetAccessPointsAsync($"missing-{Guid.NewGuid():N}");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetAccessPoints_ReturnsOnlyThePointsTheAccountAdministers()
    {
        var administered = await CreateProjectAsync();
        var contributed = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, administered, AccountRole.Administrator));
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, contributed, AccountRole.Contributor));

        var points = await Access.GetAccessPointsAsync(memberId);

        points.Select(point => point.Key).Should().Equal(administered);
    }

    [Theory]
    [InlineData("Billing")]
    [InlineData("bill.ing")]
    [InlineData("members")]
    public async Task CreateAccessPoint_InvalidProjectName_ThrowsInvalidName(string project)
    {
        await EnsureOwnerAsync();

        var act = () => Access.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = project, OwnerId = OwnerId });

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidName);
    }

    [Fact]
    public async Task CreateAccessPoint_ReservedNewOrganization_ThrowsInvalidName()
    {
        var act = () => Access.CreateAccessPointAsync(new AccessPointCreate { Organization = "access", Project = NewProjectName(), OwnerId = OwnerId });

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidName);
        (await Access.GetAccessPointAsync("access")).Should().BeNull();
    }

    [Fact]
    public async Task CreateAccessPoint_ExistingLegacyOrganization_AcceptsANewProject()
    {
        // Organizations created before the name rules keep working; only the new project name is checked.
        const string legacyOrganization = "Legacy_Org";
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        await repository.Container.UpsertItemAsync(
            new AccessPointEntity { Key = legacyOrganization, AccessMap = new() { [OwnerId] = AccountRole.Owner } },
            new PartitionKey("access"));
        await EnsureOwnerAsync();

        var point = await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = legacyOrganization, Project = NewProjectName(), OwnerId = OwnerId });

        point.Key.Should().StartWith($"{legacyOrganization}.");
    }

    [Fact]
    public async Task CreateAccount_WithoutProject_StoresAnAccountWithoutMemberships()
    {
        var id = $"acc-{Guid.NewGuid():N}";

        var account = await Access.CreateAccountAsync(new AccountCreate { IdentityId = id, UserName = $"{id}@example.com", Name = "Invitee" });

        account.Id.Should().Be(id);
        account.AccessMap.Should().BeEmpty();
        (await Access.GetAccountAsync(id))!.UserName.Should().Be($"{id}@example.com");
    }

    [Fact]
    public async Task CreateAccount_OnlyOrganization_ThrowsArgumentException()
    {
        var act = () => Access.CreateAccountAsync(new AccountCreate { IdentityId = $"acc-{Guid.NewGuid():N}", UserName = "u", Name = "n", Organization = Organization });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetMembers_ByAdministrator_ReturnsNamesOwnersFirst()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        var members = await Access.GetMembersAsync(pointKey, OwnerId);

        members.Select(member => (member.AccountId, member.Role)).Should().Equal((OwnerId, AccountRole.Owner), (memberId, AccountRole.Reader));
        members[0].Name.Should().Be("Owner");
        members[1].UserName.Should().Be($"{memberId}@example.com");
    }

    [Fact]
    public async Task GetMembers_ByContributor_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var contributorId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, contributorId, pointKey, AccountRole.Contributor));

        var act = () => Access.GetMembersAsync(pointKey, contributorId);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task SetMemberRole_LowersAndRaisesTheRoleInBothDocuments()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Administrator));

        var lowered = await Access.SetMemberRoleAsync(pointKey, memberId, AccountRole.Reader, OwnerId);

        lowered.Role.Should().Be(AccountRole.Reader);
        lowered.UserName.Should().Be($"{memberId}@example.com");
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().Contain(memberId, AccountRole.Reader);
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().Contain(pointKey, AccountRole.Reader);

        var raised = await Access.SetMemberRoleAsync(pointKey, memberId, AccountRole.ContributorWithSecrets, OwnerId);
        raised.Role.Should().Be(AccountRole.ContributorWithSecrets);
    }

    [Fact]
    public async Task SetMemberRole_OwnerByAdministrator_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var adminId = await CreateBareAccountAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, adminId, pointKey, AccountRole.Administrator));
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        var promote = () => Access.SetMemberRoleAsync(pointKey, memberId, AccountRole.Owner, adminId);
        var demoteOwner = () => Access.SetMemberRoleAsync(pointKey, OwnerId, AccountRole.Reader, adminId);

        await promote.Should().ThrowAsync<AccessDeniedException>();
        await demoteOwner.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task SetMemberRole_OwnRole_ThrowsSelfRoleChange()
    {
        var pointKey = await CreateProjectAsync();
        var adminId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, adminId, pointKey, AccountRole.Administrator));

        var act = () => Access.SetMemberRoleAsync(pointKey, adminId, AccountRole.Reader, adminId);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.SelfRoleChange);
    }

    [Fact]
    public async Task SetMemberRole_NotAMember_ThrowsMemberNotFound()
    {
        var pointKey = await CreateProjectAsync();
        var outsiderId = await CreateBareAccountAsync();

        var act = () => Access.SetMemberRoleAsync(pointKey, outsiderId, AccountRole.Reader, OwnerId);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotFound);
    }

    [Fact]
    public async Task SetMemberRole_DemoteTheOtherOwner_Succeeds()
    {
        var pointKey = await CreateProjectAsync();
        var secondOwnerId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, secondOwnerId, pointKey, AccountRole.Owner));

        var member = await Access.SetMemberRoleAsync(pointKey, secondOwnerId, AccountRole.Administrator, OwnerId);

        member.Role.Should().Be(AccountRole.Administrator);
    }

    [Fact]
    public async Task RemoveMember_ByAdministrator_RemovesFromBothDocuments()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));

        await Access.RemoveMemberAsync(pointKey, memberId, OwnerId);

        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().NotContainKey(memberId);
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().NotContainKey(pointKey);
    }

    [Fact]
    public async Task RemoveMember_Self_Leaves()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        await Access.RemoveMemberAsync(pointKey, memberId, memberId);

        (await Access.GetAccountRoleAsync(memberId, pointKey)).Should().Be(AccountRole.None);
    }

    [Fact]
    public async Task RemoveMember_OtherByContributor_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var contributorId = await CreateBareAccountAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, contributorId, pointKey, AccountRole.Contributor));
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        var act = () => Access.RemoveMemberAsync(pointKey, memberId, contributorId);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task RemoveMember_LastOwnerLeaving_ThrowsLastOwner()
    {
        var pointKey = await CreateProjectAsync();

        var act = () => Access.RemoveMemberAsync(pointKey, OwnerId, OwnerId);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.LastOwner);
    }

    [Fact]
    public async Task JoinAccessPoint_RaisesButNeverLowers()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var joined = await Access.JoinAccessPointAsync(pointKey, memberId, AccountRole.Contributor);
        var lower = await Access.JoinAccessPointAsync(pointKey, memberId, AccountRole.Reader);
        var higher = await Access.JoinAccessPointAsync(pointKey, memberId, AccountRole.Administrator);

        joined.Should().Be(AccountRole.Contributor);
        lower.Should().Be(AccountRole.Contributor);
        higher.Should().Be(AccountRole.Administrator);
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().Contain(pointKey, AccountRole.Administrator);
    }

    [Fact]
    public async Task ConcurrentGrants_BothLandInBothDocuments()
    {
        // The ETag-guarded batch retries once on a concurrent change instead of losing an update.
        var pointKey = await CreateProjectAsync();
        var firstId = await CreateBareAccountAsync();
        var secondId = await CreateBareAccountAsync();

        await Task.WhenAll(
            Access.GrantPermissionAsync(Permission(OwnerId, firstId, pointKey, AccountRole.Reader)),
            Access.GrantPermissionAsync(Permission(OwnerId, secondId, pointKey, AccountRole.Reader)));

        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().ContainKeys(firstId, secondId);
    }

    private static Permission Permission(string creatorId, string accountId, string pointKey, AccountRole role)
    {
        return new Permission
        {
            CreatedById = creatorId,
            AccountId = accountId,
            AccessPointKey = pointKey,
            Role = role,
        };
    }

    private static string NewProjectName() => $"p-{Guid.NewGuid():N}"[..14];

    private async Task EnsureOwnerAsync()
    {
        if (await Access.GetAccountAsync(OwnerId) is not null)
        {
            return;
        }

        await Access.CreateAccountAsync(new AccountCreate
        {
            IdentityId = OwnerId,
            UserName = "owner@example.com",
            Name = "Owner",
            Organization = Organization,
            Project = "base",
        });
    }

    private async Task<string> CreateProjectAsync()
    {
        await EnsureOwnerAsync();
        var point = await Access.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = Organization,
            Project = NewProjectName(),
            OwnerId = OwnerId,
        });
        return point.Key;
    }

    // An account without memberships. Created directly, because CreateAccountAsync always creates an organization.
    private async Task<string> CreateBareAccountAsync()
    {
        var id = $"acc-{Guid.NewGuid():N}";
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        await repository.Container.CreateItemAsync(
            new AccountEntity
            {
                Id = id,
                UserName = $"{id}@example.com",
                Name = id,
                AccessMap = new(),
            },
            new PartitionKey("access"));
        return id;
    }
}
