using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class AccessPointHttpTests
{
    // The admin UI and the CLI create organizations and projects without an owner; the caller owns them.
    [Fact]
    public async Task PostAccessPoints_WithoutAnOwner_CreatesTheAccessPointOwnedByTheCaller()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostAccessPoints(UserRequest("""{ "Organization": "acme", "Project": "billing" }"""));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPoint>().Key.ShouldBe("acme.billing");
        access.Created.ShouldBe(new AccessPointCreate { Organization = "acme", Project = "billing", OwnerId = "user-1" });
    }

    [Fact]
    public async Task PostAccessPoints_AnotherOwner_IsReplacedByTheCaller()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);
        var body = FunctionTestDoubles.ToJsonBody(new AccessPointCreate { Organization = "acme", OwnerId = "user-2" });

        var result = await http.PostAccessPoints(UserRequest(body));

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldBe(new AccessPointCreate { Organization = "acme", OwnerId = "user-1" });
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The organization is
    // still required.
    [Theory]
    [InlineData("", "The access point is missing.")]
    [InlineData("null", "The access point is missing.")]
    [InlineData("[1, 2]", "The request body is not an access point (at $).")]
    [InlineData("""{ "Project": "billing" }""", "The request body is not an access point (at $).")]
    public async Task PostAccessPoints_MissingOrInvalidBody_ReturnsBadRequestWithoutCreatingAnything(string body, string message)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostAccessPoints(UserRequest(body));

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Created.ShouldBeNull();
    }

    private static TestHttpRequestData UserRequest(string body)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, FunctionTestDoubles.JsonContentType(), "POST", body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    private sealed class RecordingAccess : IAccessService
    {
        // The model the handler passed to CreateAccessPointAsync; null while nothing was created.
        public AccessPointCreate? Created { get; private set; }

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model)
        {
            Created = model;
            return Task.FromResult(new AccessPoint
            {
                Key = model.Project is null ? model.Organization : $"{model.Organization}.{model.Project}",
                AccessMap = new Dictionary<string, AccountRole> { [model.OwnerId] = AccountRole.Owner },
            });
        }

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}
