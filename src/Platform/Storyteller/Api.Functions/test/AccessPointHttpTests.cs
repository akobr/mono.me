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
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

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

    // The service trims the description and puts it on the project only; the handler passes it as sent.
    [Fact]
    public async Task PostAccessPoints_WithAProjectDescription_PassesIt()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PostAccessPoints(UserRequest("""{ "Organization": "acme", "Project": "billing", "ProjectDescription": " Billing of Acme. " }"""));

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldBe(new AccessPointCreate { Organization = "acme", Project = "billing", ProjectDescription = " Billing of Acme. ", OwnerId = "user-1" });
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

    [Fact]
    public async Task PutAccessPoint_WithADescription_PassesItWithTheNormalizedKey()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PutAccessPoint(UserRequest("""{ "description": "Billing of Acme." }""", "PUT"), " ACME.Billing ");

        var point = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPoint>();
        point.Key.ShouldBe("acme.billing");
        point.Description.ShouldBe("Billing of Acme.");
        access.Updates.ShouldBe([("acme.billing", new AccessPointUpdate { Description = "Billing of Acme." })]);
    }

    // The body of an update is optional: without one, the description is cleared, as with {}.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task PutAccessPoint_MissingOrEmptyBody_ClearsTheDescription(string body)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PutAccessPoint(UserRequest(body, "PUT"), "acme.billing");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPoint>().Description.ShouldBeNull();
        access.Updates.ShouldBe([("acme.billing", new AccessPointUpdate())]);
    }

    [Theory]
    [InlineData("[1, 2]", "The request body is not a project update (at $).")]
    [InlineData("""{ "Description": 42 }""", "The request body is not a project update (at $.Description).")]
    public async Task PutAccessPoint_InvalidBody_ReturnsBadRequestWithoutCallingTheService(string body, string message)
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PutAccessPoint(UserRequest(body, "PUT"), "acme.billing");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Updates.ShouldBeEmpty();
    }

    // Only a project has a description.
    [Fact]
    public async Task PutAccessPoint_OrganizationKey_ReturnsBadRequestWithoutCallingTheService()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.PutAccessPoint(UserRequest("""{ "Description": "Acme" }""", "PUT"), "Acme");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("Access point key must be in organization.project format.");
        access.Updates.ShouldBeEmpty();
    }

    // Debug builds skip the role checks of users (DEV_AUTH), so there every user may describe a project.
    [Theory]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets)]
    public async Task PutAccessPoint_BelowAdministrator_IsAccessDenied(AccountRole role)
    {
        if (DevAuth)
        {
            return;
        }

        var access = new RecordingAccess { Role = role };
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var exception = await Should.ThrowAsync<AccessDeniedException>(
            () => http.PutAccessPoint(UserRequest("""{ "Description": "Billing" }""", "PUT"), "acme.billing"));

        exception.Message.ShouldBe("No Administrator access to 'acme.billing'.");
        access.Updates.ShouldBeEmpty();
    }

    // The refusal comes before the scope check, so a machine, which never carries User.Impersonation, gets a 403 rather
    // than a 401 in every build; no service is called, not even for the machine document.
    [Fact]
    public async Task PutAccessPoint_Machine_IsAccessDeniedWithoutCallingTheService()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var exception = await Should.ThrowAsync<AccessDeniedException>(
            () => http.PutAccessPoint(MachineRequest("""{ "Description": "Billing" }""", "PUT"), "acme.billing"));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Describing a project needs a user with the Administrator role.");
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetProjects_ReturnsTheProjectsOfTheCaller()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var result = await http.GetProjects(UserRequest(method: "GET"));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(access.Projects);
        access.Calls.ShouldBe(["projects:user-1"]);
    }

    // The service throws for an account that doesn't exist; the middleware answers 404 with it.
    [Fact]
    public async Task GetProjects_UnknownAccount_IsNotFound()
    {
        var access = new RecordingAccess { HasAccount = false };
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        await Should.ThrowAsync<NotFoundException>(() => http.GetProjects(UserRequest(method: "GET")));
    }

    [Fact]
    public async Task GetProjects_Machine_IsAccessDeniedWithoutCallingTheService()
    {
        var access = new RecordingAccess();
        var http = new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetProjects(MachineRequest()));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Listing projects needs a user.");
        access.Calls.ShouldBeEmpty();
    }

    // A user's request; a body goes as JSON, with its content type.
    private static TestHttpRequestData UserRequest(string? body = null, string method = "POST")
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is null ? null : FunctionTestDoubles.JsonContentType();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, method, body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    // A machine's request, with the scopes of a machine and no User.Impersonation.
    private static TestHttpRequestData MachineRequest(string? body = null, string method = "GET")
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is null ? null : FunctionTestDoubles.JsonContentType();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, method, body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "machine-1"),
            new("roles", Scopes.Default.Read),
            new("roles", Scopes.Default.Write),
        };
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";
        return request;
    }

    private sealed class RecordingAccess : IAccessService
    {
        public List<string> Calls { get; } = [];

        // The role of every account; checked in Release builds only.
        public AccountRole Role { get; init; } = AccountRole.Administrator;

        // False: GetProjectsAsync throws, as for an account that doesn't exist.
        public bool HasAccount { get; init; } = true;

        public IReadOnlyList<ProjectSummary> Projects { get; } =
        [
            new() { Key = "acme.billing", Role = AccountRole.Owner, Description = "Billing of Acme." },
            new() { Key = "acme.shipping", Role = AccountRole.Reader },
        ];

        // The model the handler passed to CreateAccessPointAsync; null while nothing was created.
        public AccessPointCreate? Created { get; private set; }

        // The keys and models the handler passed to UpdateAccessPointAsync.
        public List<(string Key, AccessPointUpdate Model)> Updates { get; } = [];

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model)
        {
            Created = model;
            return Task.FromResult(new AccessPoint
            {
                Key = model.Project is null ? model.Organization : $"{model.Organization}.{model.Project}",
                AccessMap = new Dictionary<string, AccountRole> { [model.OwnerId] = AccountRole.Owner },
            });
        }

        public Task<AccessPoint> UpdateAccessPointAsync(string key, AccessPointUpdate model)
        {
            Calls.Add($"update:{key}");
            Updates.Add((key, model));
            return Task.FromResult(new AccessPoint
            {
                Key = key,
                AccessMap = new Dictionary<string, AccountRole> { ["user-1"] = AccountRole.Owner },
                Description = model.Description,
            });
        }

        public Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(string accountId)
        {
            Calls.Add($"projects:{accountId}");
            return HasAccount
                ? Task.FromResult(Projects)
                : throw new NotFoundException($"The account '{accountId}' doesn't exist.");
        }

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
        {
            Calls.Add($"role:{accessPointKey}");
            return Task.FromResult(Role);
        }

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
        {
            Calls.Add($"machine:{organization}.{project}/{id}");
            return Task.FromResult<MachineAccess?>(null);
        }

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}
