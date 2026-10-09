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

public class PermissionHttpTests
{
    private const string PointKey = "org1.proj1";

    public static TheoryData<Permission> InvalidPermissions => new()
    {
        Permission(AccountRole.None),
        Permission(AccountRole.Reader) with { AccountId = " " },
        Permission(AccountRole.Reader) with { AccessPointKey = string.Empty },
    };

    [Theory]
    [MemberData(nameof(InvalidPermissions))]
    public async Task Grant_InvalidPermission_ReturnsBadRequestWithoutCallingTheService(Permission permission)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(), permission);

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldNotBeNullOrWhiteSpace();
        access.Calls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidPermissions))]
    public async Task Revoke_InvalidPermission_ReturnsBadRequestWithoutCallingTheService(Permission permission)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, NullLogger<AccessHttp>.Instance);

        var result = await http.PostRevokePermission(UserRequest(), permission);

        result.ShouldBeOfType<BadRequestObjectResult>();
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Grant_ValidPermission_UsesTheCallerAsCreator()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(), Permission(AccountRole.Contributor) with { CreatedById = "spoofed" });

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPoint>().Key.ShouldBe(PointKey);
        access.Calls.ShouldBe(["grant:user-1:user-2:Contributor", $"point:{PointKey}"]);
    }

    [Fact]
    public async Task Revoke_AccessDenied_PropagatesForTheMiddleware()
    {
        var access = new RecordingAccess { RevokeError = new AccessDeniedException("no role") };
        var http = new AccessHttp(access, NullLogger<AccessHttp>.Instance);

        await Should.ThrowAsync<AccessDeniedException>(() => http.PostRevokePermission(UserRequest(), Permission(AccountRole.Reader)));
    }

    private static Permission Permission(AccountRole role)
    {
        return new Permission
        {
            CreatedById = string.Empty,
            AccountId = "user-2",
            AccessPointKey = PointKey,
            Role = role,
        };
    }

    private static TestHttpRequestData UserRequest()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    private sealed class RecordingAccess : IAccessService
    {
        public List<string> Calls { get; } = [];

        public Exception? RevokeError { get; init; }

        public Task<bool> GrantPermissionAsync(Permission model)
        {
            Calls.Add($"grant:{model.CreatedById}:{model.AccountId}:{model.Role}");
            return Task.FromResult(true);
        }

        public Task<bool> RevokePermissionAsync(Permission model)
        {
            Calls.Add($"revoke:{model.CreatedById}:{model.AccountId}:{model.Role}");
            return RevokeError is null ? Task.FromResult(true) : Task.FromException<bool>(RevokeError);
        }

        public Task<AccessPoint?> GetAccessPointAsync(string key)
        {
            Calls.Add($"point:{key}");
            return Task.FromResult<AccessPoint?>(new AccessPoint
            {
                Key = key,
                AccessMap = new Dictionary<string, AccountRole>(),
            });
        }

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}
