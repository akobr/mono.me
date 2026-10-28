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
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(permission)));

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldNotBeNullOrWhiteSpace();
        access.Calls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidPermissions))]
    public async Task Revoke_InvalidPermission_ReturnsBadRequestWithoutCallingTheService(Permission permission)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostRevokePermission(UserRequest(FunctionTestDoubles.ToJsonBody(permission)));

        result.ShouldBeOfType<BadRequestObjectResult>();
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Grant_ValidPermission_UsesTheCallerAsCreator()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(AccountRole.Contributor) with { CreatedById = "spoofed" })));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPoint>().Key.ShouldBe(PointKey);
        access.Calls.ShouldBe(["grant:user-1:user-2:Contributor", $"point:{PointKey}"]);
    }

    [Fact]
    public async Task Revoke_AccessDenied_PropagatesForTheMiddleware()
    {
        var access = new RecordingAccess { RevokeError = new AccessDeniedException("no role") };
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        await Should.ThrowAsync<AccessDeniedException>(() => http.PostRevokePermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(AccountRole.Reader)))));
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran.
    [Theory]
    [InlineData("", "The permission is missing.")]
    [InlineData("null", "The permission is missing.")]
    [InlineData("[1, 2]", "The request body is not a permission (at $).")]
    public async Task Grant_MissingOrInvalidBody_ReturnsBadRequestWithoutCallingTheService(string body, string message)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(body));

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "The permission is missing.")]
    [InlineData("null", "The permission is missing.")]
    [InlineData("[1, 2]", "The request body is not a permission (at $).")]
    public async Task Revoke_MissingOrInvalidBody_ReturnsBadRequestWithoutCallingTheService(string body, string message)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostRevokePermission(UserRequest(body));

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task Grant_BodyWithoutAContentType_IsRead()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(AccountRole.Reader)), contentType: false));

        result.ShouldBeOfType<OkObjectResult>();
        access.Calls.ShouldBe(["grant:user-1:user-2:Reader", $"point:{PointKey}"]);
    }

    [Fact]
    public async Task Revoke_PropertyAndEnumNamesInAnyCase_AreRead()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);
        const string body = """{ "createdById": "", "accountId": "user-2", "accessPointKey": "org1.proj1", "role": "contributor" }""";

        var result = await http.PostRevokePermission(UserRequest(body));

        result.ShouldBeOfType<OkObjectResult>();
        access.Calls.ShouldBe(["revoke:user-1:user-2:Contributor", $"point:{PointKey}"]);
    }

    // Permissions are granted by users (spec 7.4): a machine is refused in every build, also one whose credentials
    // carry every scope and User.Impersonation, before the body is read or the service is asked.
    [Fact]
    public async Task GrantAndRevoke_Machine_IsAccessDeniedInEveryBuild()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);
        var body = FunctionTestDoubles.ToJsonBody(Permission(AccountRole.Owner));

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PostGrantPermission(UserOnlyRoutes.Machine("POST", body)));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PostRevokePermission(UserOnlyRoutes.Machine("POST", body)));

        access.Calls.ShouldBeEmpty();
    }

    // An actor under NoSecretReveal may not give a person secret reads: a role from ContributorWithSecrets up is refused
    // in every build, before the service is asked.
    [Theory]
    [InlineData(AccountRole.ContributorWithSecrets)]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public async Task Grant_ARoleThatReadsSecrets_ThroughARestrictedActor_IsActorRestricted(AccountRole role)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(role)), restricted: true)));

        exception.ErrorCode.ShouldBe(ErrorCodes.ActorRestricted);
        access.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    public async Task Grant_ALowerRole_ThroughARestrictedActor_IsGranted(AccountRole role)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(role)), restricted: true));

        result.ShouldBeOfType<OkObjectResult>();
        access.Calls.ShouldBe([$"grant:user-1:user-2:{role}", $"point:{PointKey}"]);
    }

    [Theory]
    [InlineData(AccountRole.ContributorWithSecrets)]
    [InlineData(AccountRole.Owner)]
    public async Task Grant_ARoleThatReadsSecrets_WithoutTheRestriction_IsGranted(AccountRole role)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        (await http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(role))))).ShouldBeOfType<OkObjectResult>();
        (await http.PostGrantPermission(UserRequest(FunctionTestDoubles.ToJsonBody(Permission(role)), restrictions: []))).ShouldBeOfType<OkObjectResult>();

        access.Calls.Count(call => call.StartsWith("grant:", StringComparison.Ordinal)).ShouldBe(2);
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

    // A user's request; the body goes as JSON, with its content type unless contentType is false. Restricted makes it
    // a call through an actor under NoSecretReveal; restrictions, through an actor with exactly those.
    private static TestHttpRequestData UserRequest(string body, bool contentType = true, bool restricted = false, string[]? restrictions = null)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, "POST", body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };

        if (restricted || restrictions is not null)
        {
            context.Items[FunctionContextItemKeys.DelegatedActor] = "mcp";
            context.Items[FunctionContextItemKeys.DelegatedRestrictions] = restrictions ?? [ActorRestrictions.NoSecretReveal];
        }

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

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();
    }
}
