using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.KeyValues.Policy;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    private IMachineAuthenticationPolicyStore MachinePolicies => Context.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();

    private IKeyValuePolicyStore KeyValuePolicies => Context.Services.GetRequiredService<IKeyValuePolicyStore>();

    private CosmosDbOptions CosmosOptions => Context.Services.GetRequiredService<IOptions<CosmosDbOptions>>().Value;

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
    public async Task Join_UndefinedRole_ThrowsArgumentException()
    {
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        var act = () => Access.JoinAccessPointAsync(pointKey, memberId, (AccountRole)42);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAccessPoint_ExistingProjectAsOrganization_ThrowsInvalidName()
    {
        // An existing project key is not an organization: "acme.web" + "x" would create a dotted project "web.x".
        var pointKey = await CreateProjectAsync();

        var act = () => Access.CreateAccessPointAsync(new AccessPointCreate { Organization = pointKey, Project = NewProjectName(), OwnerId = OwnerId });

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidName);
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

    [Fact]
    public async Task CreateAccessPoint_NewOrganizationWithDescription_TrimsItOntoTheProjectOnly()
    {
        await EnsureOwnerAsync();
        var organization = NewOrganizationName();

        var point = await Access.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = organization,
            Project = NewProjectName(),
            ProjectDescription = "  Billing of the customers.\n",
            OwnerId = OwnerId,
        });

        point.Description.Should().Be("Billing of the customers.");
        (await Access.GetAccessPointAsync(point.Key))!.Description.Should().Be("Billing of the customers.");
        (await Access.GetAccessPointAsync(organization))!.Description.Should().BeNull();
        (await IsDescriptionStoredAsync(organization)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task CreateAccessPoint_BlankDescription_IsNotStored(string description)
    {
        await EnsureOwnerAsync();

        var point = await Access.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = Organization,
            Project = NewProjectName(),
            ProjectDescription = description,
            OwnerId = OwnerId,
        });

        point.Description.Should().BeNull();
        (await IsDescriptionStoredAsync(point.Key)).Should().BeFalse();
    }

    [Fact]
    public async Task CreateAccount_WithProjectDescription_DescribesOnlyTheProject()
    {
        var id = $"acc-{Guid.NewGuid():N}";
        var organization = NewOrganizationName();
        var project = NewProjectName();
        var pointKey = $"{organization}.{project}";

        var account = await Access.CreateAccountAsync(new AccountCreate
        {
            IdentityId = id,
            UserName = $"{id}@example.com",
            Name = "Founder",
            Organization = organization,
            Project = project,
            ProjectDescription = " Shipping of the orders. ",
        });

        account.AccessMap.Should().Contain(pointKey, AccountRole.Owner);
        (await Access.GetAccessPointAsync(pointKey))!.Description.Should().Be("Shipping of the orders.");
        (await Access.GetAccessPointAsync(organization))!.Description.Should().BeNull();
        (await IsDescriptionStoredAsync(organization)).Should().BeFalse();
        var accountDocument = await ReadDocumentAsync(id);
        accountDocument.TryGetProperty("ProjectDescription", out _).Should().BeFalse();
        accountDocument.TryGetProperty("Description", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAccessPoint_SetsReplacesAndClearsTheDescription()
    {
        var pointKey = await CreateProjectAsync();

        var set = await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "  First.  " });
        set.Description.Should().Be("First.");
        (await Access.GetAccessPointAsync(pointKey))!.Description.Should().Be("First.");

        var replaced = await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Second." });
        replaced.Description.Should().Be("Second.");
        (await Access.GetAccessPointAsync(pointKey))!.Description.Should().Be("Second.");

        var cleared = await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate());
        cleared.Description.Should().BeNull();
        (await IsDescriptionStoredAsync(pointKey)).Should().BeFalse();

        await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Third." });
        var blank = await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = " \n " });
        blank.Description.Should().BeNull();
        (await IsDescriptionStoredAsync(pointKey)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAccessPoint_KeepsTheMembersAndThePolicies()
    {
        var pointKey = await CreateProjectAsync();
        var project = ProjectOf(pointKey);
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));
        await MachinePolicies.SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate });
        await KeyValuePolicies.SetAsync(Organization, project, new KeyValuePolicy { RequireMtlsForSecretDelivery = false, Version = 1 });

        var point = await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Described." });

        point.AccessMap.Should().Contain(memberId, AccountRole.Reader);
        var stored = await ReadPointEntityAsync(pointKey);
        stored.Description.Should().Be("Described.");
        stored.AccessMap.Should().Contain(OwnerId, AccountRole.Owner).And.Contain(memberId, AccountRole.Reader);
        stored.MachineAuthentication!.CredentialKind.Should().Be(MachineCredentialKind.Certificate);
        stored.KeyValues!.Version.Should().Be(1UL);
        stored.KeyValues.RequireMtlsForSecretDelivery.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAccessPoint_UnknownProject_ThrowsNotFound()
    {
        var act = () => Access.UpdateAccessPointAsync($"{Organization}.{NewProjectName()}", new AccessPointUpdate { Description = "Nowhere." });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAccessPoint_OrganizationKey_ThrowsArgumentException()
    {
        await EnsureOwnerAsync();

        var act = () => Access.UpdateAccessPointAsync(Organization, new AccessPointUpdate { Description = "An organization." });

        await act.Should().ThrowAsync<ArgumentException>();
        (await IsDescriptionStoredAsync(Organization)).Should().BeFalse();
    }

    [Fact]
    public async Task LaterWrites_KeepTheDescription()
    {
        // A grant, a key/value policy and a machine policy each replace the whole document.
        var pointKey = await CreateProjectAsync("Kept.");
        var project = ProjectOf(pointKey);
        var memberId = await CreateBareAccountAsync();

        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Contributor));
        (await Access.GetAccessPointAsync(pointKey))!.Description.Should().Be("Kept.");

        await KeyValuePolicies.SetAsync(Organization, project, new KeyValuePolicy { RequireMtlsForSecretDelivery = false, Version = 1 });
        (await Access.GetAccessPointAsync(pointKey))!.Description.Should().Be("Kept.");

        await MachinePolicies.SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });
        (await Access.GetAccessPointAsync(pointKey))!.Description.Should().Be("Kept.");
    }

    [Fact]
    public async Task ConcurrentGrantAndDescription_BothLand()
    {
        // Whichever write commits second retries on the ETag of the project. That is mostly the grant, whose batch
        // needs one round trip more; the next tests put a grant between the update's read and its replace.
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();

        await Task.WhenAll(
            Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader)),
            Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Raced." }));

        var point = await Access.GetAccessPointAsync(pointKey);
        point!.AccessMap.Should().ContainKey(memberId);
        point.Description.Should().Be("Raced.");
    }

    [Fact]
    public async Task UpdateAccessPoint_GrantBeforeReplace_RetriesAndKeepsTheMember()
    {
        // The replace fails on the ETag, and the retry reads the new member.
        var pointKey = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        var interceptor = new ReplaceInterceptor($"apt.{pointKey}", async replace =>
        {
            if (replace == 1)
            {
                await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));
            }
        });
        using var client = interceptor.CreateClient(CosmosOptions);

        var updated = await AccessOn(client).UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Raced." });

        interceptor.Replaces.Should().Be(2);
        updated.AccessMap.Should().ContainKey(memberId);
        var point = await Access.GetAccessPointAsync(pointKey);
        point!.AccessMap.Should().Contain(memberId, AccountRole.Reader);
        point.Description.Should().Be("Raced.");
        (await Access.GetAccountAsync(memberId))!.AccessMap.Should().Contain(pointKey, AccountRole.Reader);
    }

    [Fact]
    public async Task UpdateAccessPoint_ChangedBeforeEveryReplace_ThrowsConflict()
    {
        var pointKey = await CreateProjectAsync("Before.");
        var memberIds = new[] { await CreateBareAccountAsync(), await CreateBareAccountAsync() };
        var interceptor = new ReplaceInterceptor($"apt.{pointKey}", replace =>
            Access.GrantPermissionAsync(Permission(OwnerId, memberIds[replace - 1], pointKey, AccountRole.Reader)));
        using var client = interceptor.CreateClient(CosmosOptions);

        var act = () => AccessOn(client).UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Raced." });

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        interceptor.Replaces.Should().Be(2);
        var point = await Access.GetAccessPointAsync(pointKey);
        point!.AccessMap.Should().ContainKeys(memberIds);
        point.Description.Should().Be("Before.");
    }

    [Fact]
    public async Task GetProjects_ListsTheProjectsOfEveryRoleWithTheirDescriptions()
    {
        var memberId = await CreateBareAccountAsync();
        var expected = new List<ProjectSummary>();

        foreach (var role in new[] { AccountRole.Reader, AccountRole.Contributor, AccountRole.ContributorWithSecrets, AccountRole.Administrator, AccountRole.Owner })
        {
            // two of the projects have no description
            var description = role is AccountRole.Contributor or AccountRole.Administrator ? null : $"A project of a {role}.";
            var pointKey = await CreateProjectAsync(description);
            await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, role));
            expected.Add(new ProjectSummary { Key = pointKey, Role = role, Description = description });
        }

        // a role on the organization is not a project
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, Organization, AccountRole.Reader));

        var projects = await Access.GetProjectsAsync(memberId);

        projects.Should().Equal(expected.OrderBy(project => project.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetProjects_MissingAccessPoint_IsLeftOut()
    {
        var pointKey = await CreateProjectAsync("Present.");
        var memberId = await CreateBareAccountAsync(new() { [$"{Organization}.{NewProjectName()}"] = AccountRole.Reader });
        await Access.GrantPermissionAsync(Permission(OwnerId, memberId, pointKey, AccountRole.Reader));

        var projects = await Access.GetProjectsAsync(memberId);

        projects.Should().Equal(new ProjectSummary { Key = pointKey, Role = AccountRole.Reader, Description = "Present." });
    }

    [Fact]
    public async Task GetProjects_UnknownAccount_ThrowsNotFound()
    {
        var act = () => Access.GetProjectsAsync($"missing-{Guid.NewGuid():N}");

        await act.Should().ThrowAsync<NotFoundException>();
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

    // The access service on a client of its own, for example one with a ReplaceInterceptor.
    private CosmosAccessService AccessOn(CosmosClient client)
    {
        return new CosmosAccessService(
            new ContainerRepositoryProvider(new FixedClientProvider(client)),
            Context.Services.GetRequiredService<IContainerFactory>(),
            Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>());
    }

    // A new organization creates its own container, so the tests use few of them.
    private static string NewOrganizationName() => $"o-{Guid.NewGuid():N}"[..14];

    private static string ProjectOf(string pointKey) => pointKey[(Organization.Length + 1)..];

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

    private async Task<string> CreateProjectAsync(string description = null)
    {
        await EnsureOwnerAsync();
        var point = await Access.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = Organization,
            Project = NewProjectName(),
            ProjectDescription = description,
            OwnerId = OwnerId,
        });
        return point.Key;
    }

    // An account without memberships, or with a map the access points don't know of.
    // Created directly, because CreateAccountAsync always creates an organization.
    private async Task<string> CreateBareAccountAsync(Dictionary<string, AccountRole> accessMap = null)
    {
        var id = $"acc-{Guid.NewGuid():N}";
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        await repository.Container.CreateItemAsync(
            new AccountEntity
            {
                Id = id,
                UserName = $"{id}@example.com",
                Name = id,
                AccessMap = accessMap ?? new(),
            },
            new PartitionKey("access"));
        return id;
    }

    // The stored document as written: the Cosmos client leaves out null values.
    private async Task<JsonElement> ReadDocumentAsync(string id)
    {
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        using var response = await repository.Container.ReadItemStreamAsync(id, new PartitionKey("access"));
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(response.Content);
        return document.RootElement.Clone();
    }

    private async Task<bool> IsDescriptionStoredAsync(string pointKey)
    {
        return (await ReadDocumentAsync($"apt.{pointKey}")).TryGetProperty("Description", out _);
    }

    // The whole entity, with the key/value policy the AccessPoint model leaves out.
    private async Task<AccessPointEntity> ReadPointEntityAsync(string pointKey)
    {
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        var options = Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
        return await repository.Container.TryReadItemAsync(
            $"apt.{pointKey}",
            new PartitionKey("access"),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(options));
    }

    private sealed class FixedClientProvider(CosmosClient client) : ICosmosClientProvider
    {
        public CosmosClient Client => client;
    }
}
