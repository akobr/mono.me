using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ApiAccountCreate = _42.Platform.Storyteller.Api.V1.Models.AccountCreate;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MembersHttpTests
{
    private const string EntraAccountId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";

    [Fact]
    public async Task SetMemberRole_UsesTheCallerAndANormalizedKey()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

        var result = await http.SetMemberRole(UserRequest(FunctionTestDoubles.ToJsonBody(new MemberRoleUpdate { Role = AccountRole.Reader })), " ACME.Billing ", "user-2");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AccessPointMember>().Role.ShouldBe(AccountRole.Reader);
        access.Calls.ShouldBe(["set:acme.billing:user-2:Reader:user-1"]);
    }

    [Theory]
    [InlineData(AccountRole.None)]
    [InlineData((AccountRole)42)]
    public async Task SetMemberRole_InvalidRole_ReturnsBadRequest(AccountRole role)
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

        var result = await http.SetMemberRole(UserRequest(FunctionTestDoubles.ToJsonBody(new MemberRoleUpdate { Role = role })), "acme.billing", "user-2");

        result.ShouldBeOfType<BadRequestObjectResult>();
        access.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The role is required,
    // so {} is not a member role either.
    [Theory]
    [InlineData("", "A role other than None is required; remove the member instead.")]
    [InlineData("null", "A role other than None is required; remove the member instead.")]
    [InlineData("[1, 2]", "The request body is not a member role (at $).")]
    [InlineData("{}", "The request body is not a member role (at $).")]
    public async Task SetMemberRole_MissingOrInvalidBody_ReturnsBadRequestWithoutCallingTheService(string body, string message)
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

        var result = await http.SetMemberRole(UserRequest(body), "acme.billing", "user-2");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task SetMemberRole_BodyWithoutAContentType_IsRead()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

        var result = await http.SetMemberRole(UserRequest(FunctionTestDoubles.ToJsonBody(new MemberRoleUpdate { Role = AccountRole.Contributor }), contentType: false), "acme.billing", "user-2");

        result.ShouldBeOfType<OkObjectResult>();
        access.Calls.ShouldBe(["set:acme.billing:user-2:Contributor:user-1"]);
    }

    [Fact]
    public async Task SetMemberRole_PropertyAndEnumNamesInAnyCase_AreRead()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

        var result = await http.SetMemberRole(UserRequest("""{ "role": "administrator" }"""), "acme.billing", "user-2");

        result.ShouldBeOfType<OkObjectResult>();
        access.Calls.ShouldBe(["set:acme.billing:user-2:Administrator:user-1"]);
    }

    [Fact]
    public async Task RemoveMember_ReturnsNoContent()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

        var result = await http.RemoveMember(UserRequest(), "acme.billing", "user-1");

        result.ShouldBeOfType<NoContentResult>();
        access.Calls.ShouldBe(["remove:acme.billing:user-1:user-1"]);
    }

    [Fact]
    public async Task GetMembers_PassesTheCaller()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);

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
        var http = AccountHttp(access);

        var result = await http.PostAccount(UserRequest(FunctionTestDoubles.ToJsonBody(new ApiAccountCreate { Organization = organization, Project = project })));

        result.ShouldBeOfType<BadRequestObjectResult>();
        access.Calls.ShouldBe(["account:user-1"]);
    }

    [Fact]
    public async Task PostAccount_WithoutOrganizationAndProject_CreatesABareAccount()
    {
        var access = new RecordingAccess();
        var http = AccountHttp(access);

        var result = await http.PostAccount(UserRequest(FunctionTestDoubles.ToJsonBody(new ApiAccountCreate())));

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.Organization.ShouldBeNull();
        access.Created.Project.ShouldBeNull();
        access.Created.UserName.ShouldBe("ada@example.com");
    }

    // The service trims the description and puts it on the project only; the handler passes it as sent.
    [Fact]
    public async Task PostAccount_WithAProjectDescription_PassesIt()
    {
        var access = new RecordingAccess();
        var http = AccountHttp(access);
        var body = FunctionTestDoubles.ToJsonBody(new ApiAccountCreate { Organization = "acme", Project = "billing", ProjectDescription = " Billing of Acme. " });

        var result = await http.PostAccount(UserRequest(body));

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.Organization.ShouldBe("acme");
        access.Created.Project.ShouldBe("billing");
        access.Created.ProjectDescription.ShouldBe(" Billing of Acme. ");
    }

    // The description belongs to the project the account creates; without one, it has nowhere to go.
    [Fact]
    public async Task PostAccount_ProjectDescriptionWithoutOrganizationAndProject_ReturnsBadRequest()
    {
        var access = new RecordingAccess();
        var http = AccountHttp(access);

        var result = await http.PostAccount(UserRequest(FunctionTestDoubles.ToJsonBody(new ApiAccountCreate { ProjectDescription = "Billing of Acme." })));

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("A project description needs an organization and a project.");
        access.Calls.ShouldBe(["account:user-1"]);
        access.Created.ShouldBeNull();
    }

    // A blank description is no description, so the bare account is created.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task PostAccount_BlankProjectDescriptionWithoutOrganizationAndProject_CreatesABareAccount(string description)
    {
        var access = new RecordingAccess();
        var http = AccountHttp(access);

        var result = await http.PostAccount(UserRequest(FunctionTestDoubles.ToJsonBody(new ApiAccountCreate { ProjectDescription = description })));

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.Project.ShouldBeNull();
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. An account without
    // memberships is created with {}, not without a body.
    [Theory]
    [InlineData("", "The account is missing.")]
    [InlineData("null", "The account is missing.")]
    [InlineData("[1, 2]", "The request body is not an account (at $).")]
    public async Task PostAccount_MissingOrInvalidBody_ReturnsBadRequestWithoutCreatingAnAccount(string body, string message)
    {
        var access = new RecordingAccess();
        var http = AccountHttp(access);

        var result = await http.PostAccount(UserRequest(body));

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Calls.ShouldBe(["account:user-1"]);
        access.Created.ShouldBeNull();
    }

    // The Entra ID modules write sub as {tid}:{oid}, the id the account is created under.
    [Fact]
    public async Task PostAccount_EntraId_CreatesTheAccountUnderTheTenantAndObjectId()
    {
        var access = new RecordingAccess();
        var http = AccountHttp(access, IdentityProviderKind.EntraId);

        var result = await http.PostAccount(UserRequest(FunctionTestDoubles.ToJsonBody(new ApiAccountCreate()), userId: EntraAccountId));

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull().IdentityId.ShouldBe(EntraAccountId);
    }

    // Any other id under Entra ID means a token passed without the rewrite: a programming error, before any lookup.
    // Without the options, the provider is Entra ID, the default of Auth:Provider.
    [Theory]
    [InlineData("user-1", true)]
    [InlineData("user-1", false)]
    [InlineData("8A7B6C5D-4E3F-4A1B-8C9D-0E1F2A3B4C5D:3F2C6A8E-6C1D-4A59-9A47-0A2F4F4C1B2D", true)]
    [InlineData("8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d", true)]
    public async Task PostAccount_EntraIdWithAnotherId_IsAProgrammingError(string userId, bool withOptions)
    {
        var access = new RecordingAccess();
        var http = withOptions
            ? AccountHttp(access, IdentityProviderKind.EntraId)
            : new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => http.PostAccount(UserRequest(FunctionTestDoubles.ToJsonBody(new ApiAccountCreate()), userId: userId)));

        access.Calls.ShouldBeEmpty();
        access.Created.ShouldBeNull();
    }

    // Members are managed by users (spec 7.4): a machine is refused in every build, also one whose credentials carry
    // every scope and User.Impersonation, before the service is asked.
    [Fact]
    public async Task MemberRoutes_Machine_IsAccessDeniedInEveryBuild()
    {
        var access = new RecordingAccess();
        var http = new MembersHttp(access, FunctionTestDoubles.ApiJsonOptions);
        var body = FunctionTestDoubles.ToJsonBody(new MemberRoleUpdate { Role = AccountRole.Owner });

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetMembers(UserOnlyRoutes.Machine(), "acme"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.SetMemberRole(UserOnlyRoutes.Machine("PUT", body), "acme.billing", "user-2"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.RemoveMember(UserOnlyRoutes.Machine("DELETE"), "acme.billing", "user-2"));

        access.Calls.ShouldBeEmpty();
    }

    // The handler of account registration under the provider; AuthKit keys accounts by the sub as it is.
    private static AccessHttp AccountHttp(RecordingAccess access, IdentityProviderKind provider = IdentityProviderKind.AuthKit)
    {
        return new AccessHttp(
            access,
            FunctionTestDoubles.ApiJsonOptions,
            NullLogger<AccessHttp>.Instance,
            authOptions: Options.Create(new UserAuthenticationOptions { Provider = provider }));
    }

    // A user's request; a body goes as JSON, with its content type unless contentType is false.
    private static TestHttpRequestData UserRequest(string? body = null, bool contentType = true, string userId = "user-1")
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is not null && contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", userId),
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
