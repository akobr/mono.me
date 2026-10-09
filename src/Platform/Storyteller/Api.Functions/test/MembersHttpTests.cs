using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using ApiAccountCreate = _42.Platform.Storyteller.Api.V1.Models.AccountCreate;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MembersHttpTests
{
    [Fact]
    public async Task SetMemberRole_UsesTheCallerAndANormalizedKey()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access);

        var result = await http.SetMemberRole(UserRequest(), new MemberRoleUpdate { Role = AccountRole.Reader }, " ACME.Billing ", "user-2");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPointMember>().Role.ShouldBe(AccountRole.Reader);
        access.Calls.ShouldBe(["set:acme.billing:user-2:Reader:user-1"]);
    }

    [Theory]
    [InlineData(AccountRole.None)]
    [InlineData((AccountRole)42)]
    public async Task SetMemberRole_InvalidRole_ReturnsBadRequest(AccountRole role)
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access);

        var result = await http.SetMemberRole(UserRequest(), new MemberRoleUpdate { Role = role }, "acme.billing", "user-2");

        result.ShouldBeOfType<BadRequestObjectResult>();
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveMember_ReturnsNoContent()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access);

        var result = await http.RemoveMember(UserRequest(), "acme.billing", "user-1");

        result.ShouldBeOfType<NoContentResult>();
        access.Calls.ShouldBe(["remove:acme.billing:user-1:user-1"]);
    }

    [Fact]
    public async Task GetMembers_PassesTheCaller()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access);

        var result = await http.GetMembers(UserRequest(), "acme");

        result.ShouldBeOfType<OkObjectResult>();
        access.Calls.ShouldBe(["members:acme:user-1"]);
    }

    [Theory]
    [InlineData("acme", null)]
    [InlineData(null, "billing")]
    public async Task PostAccount_OnlyOneOfOrganizationAndProject_ReturnsBadRequest(string? organization, string? project)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, NullLogger<AccessHttp>.Instance);

        var result = await http.PostAccount(UserRequest(), new ApiAccountCreate { Organization = organization, Project = project });

        result.ShouldBeOfType<BadRequestObjectResult>();
        access.Calls.ShouldBe(["account:user-1"]);
    }

    [Fact]
    public async Task PostAccount_WithoutOrganizationAndProject_CreatesABareAccount()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, NullLogger<AccessHttp>.Instance);

        var result = await http.PostAccount(UserRequest(), new ApiAccountCreate());

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.Organization.ShouldBeNull();
        access.Created.Project.ShouldBeNull();
        access.Created.UserName.ShouldBe("ada@example.com");
    }

    private static TestHttpRequestData UserRequest()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("preferred_username", "ada@example.com"),
            new("name", "Ada"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    private sealed class RecordingAccess : IAccessService
    {
        public List<string> Calls { get; } = [];

        public AccountCreate? Created { get; private set; }

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId)
        {
            Calls.Add($"members:{accessPointKey}:{actorId}");
            return Task.FromResult<IReadOnlyList<AccessPointMember>>([]);
        }

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId)
        {
            Calls.Add($"set:{accessPointKey}:{accountId}:{role}:{actorId}");
            return Task.FromResult(new AccessPointMember { AccountId = accountId, Role = role });
        }

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId)
        {
            Calls.Add($"remove:{accessPointKey}:{accountId}:{actorId}");
            return Task.CompletedTask;
        }

        public Task<Account?> GetAccountAsync(string id)
        {
            Calls.Add($"account:{id}");
            return Task.FromResult<Account?>(null);
        }

        public Task<Account> CreateAccountAsync(AccountCreate model)
        {
            Created = model;
            return Task.FromResult(new Account
            {
                Id = model.IdentityId,
                UserName = model.UserName,
                Name = model.Name,
                AccessMap = new Dictionary<string, AccountRole>(),
            });
        }

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}
