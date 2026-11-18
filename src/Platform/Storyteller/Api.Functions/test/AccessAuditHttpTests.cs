using System.Collections.Specialized;
using System.Net;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// GET v1/access/points/{key}/audit (spec "Append-Only Access Audit Trail", 6): the filters of the query as the log gets
// them, the project of a project key, users only, and the Administrator role on the exact key. Debug builds (DEV_AUTH)
// skip the role checks of users unless a test enforces them (UserRoleChecks).
public class AccessAuditHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string ProjectKey = "acme.billing";
    private const string MachineMessage = "Reading the access audit needs a user with the Administrator role.";

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    // The whole trail of the organization: no project, and the page of the log as it is.
    [Fact]
    public async Task OrganizationKey_PassesTheOrganizationAndNoProject_AndAnswersThePageOfTheLog()
    {
        var (http, log, _) = Create();

        var result = await http.GetAccessAudit(Request(), Org);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(log.Page);
        log.Calls.ShouldBe([(Org, new AccessAuditQuery())]);
    }

    [Theory]
    [InlineData("billing", "billing")]
    [InlineData(" Billing ", "billing")]
    [InlineData("BILLING", "billing")]
    [InlineData(" ", null)]
    [InlineData("", null)]
    public async Task OrganizationKey_WithAProject_NarrowsTheQueryToIt(string project, string? expected)
    {
        var (http, log, _) = Create();

        var result = await http.GetAccessAudit(Request(q => q["project"] = project), Org);

        result.ShouldBeOfType<OkObjectResult>();
        log.Calls.ShouldBe([(Org, new AccessAuditQuery { Project = expected })]);
    }

    // The key is trimmed and lower-cased; the log gets its organization, the query its project, and the role is looked
    // up on the project.
    [Theory]
    [InlineData("acme.billing", null)]
    [InlineData(" ACME.Billing ", null)]
    [InlineData("acme.billing", "billing")]
    [InlineData("acme.billing", " BILLING ")]
    public async Task ProjectKey_PassesTheOrganizationAndTheProjectOfTheKey(string key, string? project)
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, log, access) = Create();

        var result = await http.GetAccessAudit(Request(q => q["project"] = project), key);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(log.Page);
        log.Calls.ShouldBe([(Org, new AccessAuditQuery { Project = Project })]);
        access.Lookups.ShouldBe([ProjectKey]);
    }

    [Theory]
    [InlineData("payroll")]
    [InlineData(" Payroll ")]
    public async Task ProjectKey_WithAnotherProject_IsInvalidQuery_AndReadsNoLog(string project)
    {
        var (http, log, _) = Create();

        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetAccessAudit(Request(q => q["project"] = project), ProjectKey));

        exception.Message.ShouldBe("The project 'payroll' of the query is not the project of the key 'acme.billing'.");
        ShouldBeBadRequest(exception, ErrorCodes.InvalidQuery);
        log.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("MemberGranted,MemberRevoked")]
    [InlineData("MemberGranted, MemberRevoked")]
    [InlineData(",MemberGranted")]
    public async Task Action_WithAComma_IsInvalidQuery_AndReadsNoLog(string action)
    {
        var (http, log, _) = Create();

        // Enum.TryParse would combine the names into the value of their flags, here MemberRevoked
        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetAccessAudit(Request(q => q["action"] = action), Org));

        ShouldBeBadRequest(exception, ErrorCodes.InvalidQuery);
        log.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task From_LaterThanTo_IsInvalidQuery_AndReadsNoLog()
    {
        var (http, log, _) = Create();

        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetAccessAudit(
            Request(q =>
            {
                q["from"] = "2026-11-02T00:00:00Z";
                q["to"] = "2026-11-01T00:00:00Z";
            }),
            Org));

        exception.Message.ShouldBe("The query parameter 'from' must not be later than 'to'.");
        ShouldBeBadRequest(exception, ErrorCodes.InvalidQuery);
        log.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("MemberGranted", AccessAuditAction.MemberGranted)]
    [InlineData("membergranted", AccessAuditAction.MemberGranted)]
    [InlineData("MACHINERESET", AccessAuditAction.MachineReset)]
    [InlineData("keyValuePolicyChanged", AccessAuditAction.KeyValuePolicyChanged)]
    [InlineData("SharedCertificateRevoked", AccessAuditAction.SharedCertificateRevoked)]
    public async Task Action_InAnyCase_IsParsed(string action, AccessAuditAction expected)
    {
        var (http, log, _) = Create();

        var result = await http.GetAccessAudit(Request(q => q["action"] = action), Org);

        result.ShouldBeOfType<OkObjectResult>();
        log.Calls.ShouldBe([(Org, new AccessAuditQuery { Action = expected })]);
    }

    [Theory]
    [InlineData("MemberAdded")]
    [InlineData("Member Granted")]
    [InlineData("99")]
    public async Task Action_Unknown_IsInvalidQuery_AndReadsNoLog(string action)
    {
        var (http, log, _) = Create();

        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetAccessAudit(Request(q => q["action"] = action), Org));

        exception.Message.ShouldBe($"The query parameter 'action' must be {string.Join(", ", Enum.GetNames<AccessAuditAction>())}.");
        ShouldBeBadRequest(exception, ErrorCodes.InvalidQuery);
        log.Calls.ShouldBeEmpty();
    }

    // The target is trimmed, the times are ISO 8601 (a date is its midnight in UTC) and the token goes to the log as it
    // came, which decodes it.
    [Fact]
    public async Task TargetTimesAndContinuationToken_PassThrough()
    {
        var (http, log, _) = Create();

        var result = await http.GetAccessAudit(
            Request(q =>
            {
                q["project"] = Project;
                q["action"] = "memberRoleChanged";
                q["target"] = " account-2 ";
                q["from"] = "2026-11-01";
                q["to"] = "2026-11-15T10:30:00+01:00";
                q["continuationToken"] = "MTphY2EuMDAx";
            }),
            Org);

        var expected = new AccessAuditQuery
        {
            Project = Project,
            Action = AccessAuditAction.MemberRoleChanged,
            Target = "account-2",
            From = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.FromHours(1)),
            ContinuationToken = "MTphY2EuMDAx",
        };

        result.ShouldBeOfType<OkObjectResult>();
        log.Calls.ShouldBe([(Org, expected)]);
        log.Calls[0].Query.PageSize.ShouldBe(AccessAuditQuery.MaxPageSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Target_Blank_IsLeftOut(string target)
    {
        var (http, log, _) = Create();

        await http.GetAccessAudit(Request(q => q["target"] = target), Org);

        log.Calls.ShouldBe([(Org, new AccessAuditQuery())]);
    }

    [Theory]
    [InlineData("from", "yesterday")]
    [InlineData("to", "2026-13-01")]
    public async Task Time_NotIso_IsInvalidQuery_AndReadsNoLog(string parameter, string value)
    {
        var (http, log, _) = Create();

        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetAccessAudit(Request(q => q[parameter] = value), Org));

        exception.Message.ShouldBe($"The query parameter '{parameter}' must be an ISO 8601 date or time.");
        ShouldBeBadRequest(exception, ErrorCodes.InvalidQuery);
        log.Calls.ShouldBeEmpty();
    }

    // The log refuses a token it did not issue; the route answers it as any bad filter.
    [Fact]
    public async Task ContinuationToken_RefusedByTheLog_IsInvalidQuery()
    {
        var (http, log, _) = Create();
        log.Refusal = new InvalidInputException("The continuation token is not one of this access audit trail.", ErrorCodes.InvalidQuery);

        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetAccessAudit(Request(q => q["continuationToken"] = "bogus"), Org));

        ShouldBeBadRequest(exception, ErrorCodes.InvalidQuery);
        log.Calls.ShouldBe([(Org, new AccessAuditQuery { ContinuationToken = "bogus" })]);
    }

    // Users only (spec 7.4): a machine is refused in every build, also one whose credentials carry every scope and
    // User.Impersonation and whose account would be an Owner, before a role is looked up or the log is read.
    [Fact]
    public async Task Machine_IsAccessDeniedInEveryBuild()
    {
        var (http, log, access) = Create(fallback: AccountRole.Owner);

        foreach (var key in new[] { Org, ProjectKey })
        {
            await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetAccessAudit(UserOnlyRoutes.Machine(), key), MachineMessage);
            await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetAccessAudit(TestRequests.Machine(), key), MachineMessage);
        }

        var denied = await Should.ThrowAsync<AccessDeniedException>(() => http.GetAccessAudit(TestRequests.Machine(), Org));
        var response = ErrorResponseMapping.ToActionResult(denied).ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe((int)HttpStatusCode.Forbidden);
        response.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.AccessDenied);

        log.Calls.ShouldBeEmpty();
        access.Lookups.ShouldBeEmpty();
    }

    // Data scopes in a user token do not stand in for User.Impersonation: 401 in Release builds. Debug builds (DEV_AUTH)
    // skip the check and serve the request.
    [Fact]
    public async Task TokenThatDoesNotActForAUser_IsUnauthorized()
    {
        var (http, log, _) = Create();

        if (DevAuth)
        {
            (await http.GetAccessAudit(UserOnlyRoutes.NotActingForAUser(), Org)).ShouldBeOfType<OkObjectResult>();
            return;
        }

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => http.GetAccessAudit(UserOnlyRoutes.NotActingForAUser(), Org));

        exception.Message.ShouldBe(UserOnlyRoutes.NotActingForAUserMessage);
        log.Calls.ShouldBeEmpty();
    }

    // The role checks of a Release build, in every build: the Owners and Administrators of the key read its trail, every
    // other role gets 403 AccessDenied before the log is read, on an organization key and on a project key alike.
    [Theory]
    [InlineData(AccountRole.None, false)]
    [InlineData(AccountRole.Reader, false)]
    [InlineData(AccountRole.Contributor, false)]
    [InlineData(AccountRole.ContributorWithSecrets, false)]
    [InlineData(AccountRole.Administrator, true)]
    [InlineData(AccountRole.Owner, true)]
    public async Task RoleChecksEnforced_NeedAnAdministratorOfTheKey(AccountRole role, bool allowed)
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, log, access) = Create(new Dictionary<string, AccountRole> { [Org] = role, [ProjectKey] = role });

        if (allowed)
        {
            (await http.GetAccessAudit(Request(), Org)).ShouldBeOfType<OkObjectResult>();
            (await http.GetAccessAudit(Request(), ProjectKey)).ShouldBeOfType<OkObjectResult>();

            log.Calls.ShouldBe([(Org, new AccessAuditQuery()), (Org, new AccessAuditQuery { Project = Project })]);
            access.Lookups.ShouldBe([Org, ProjectKey]);
            return;
        }

        foreach (var key in new[] { Org, ProjectKey })
        {
            var denied = await Should.ThrowAsync<AccessDeniedException>(() => http.GetAccessAudit(Request(), key));
            denied.Message.ShouldBe($"No Administrator access to '{key}'.");

            var response = ErrorResponseMapping.ToActionResult(denied).ShouldBeOfType<ObjectResult>();
            response.StatusCode.ShouldBe((int)HttpStatusCode.Forbidden);
            response.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        }

        log.Calls.ShouldBeEmpty();
        access.Lookups.ShouldBe([Org, ProjectKey]);
    }

    // The exact role on the key (spec 11.6): an Administrator of the organization without a role on the project reads the
    // project's records through the organization key, not through the project key.
    [Fact]
    public async Task OrganizationAdministrator_WithoutARoleOnTheProject_ReadsItThroughTheOrganizationKeyOnly()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, log, access) = Create(new Dictionary<string, AccountRole> { [Org] = AccountRole.Administrator });

        var denied = await Should.ThrowAsync<AccessDeniedException>(() => http.GetAccessAudit(Request(), ProjectKey));
        var result = await http.GetAccessAudit(Request(q => q["project"] = Project), Org);

        denied.Message.ShouldBe("No Administrator access to 'acme.billing'.");
        denied.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        result.ShouldBeOfType<OkObjectResult>();
        log.Calls.ShouldBe([(Org, new AccessAuditQuery { Project = Project })]);
        access.Lookups.ShouldBe([ProjectKey, Org]);
    }

    // And the other way round: an Administrator of the project alone reads its records through the project key, not the
    // organization's trail, also not narrowed to the project.
    [Fact]
    public async Task ProjectAdministrator_WithoutARoleOnTheOrganization_ReadsOnlyThroughTheProjectKey()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, log, access) = Create(new Dictionary<string, AccountRole> { [ProjectKey] = AccountRole.Administrator });

        var denied = await Should.ThrowAsync<AccessDeniedException>(() => http.GetAccessAudit(Request(q => q["project"] = Project), Org));
        var result = await http.GetAccessAudit(Request(), ProjectKey);

        denied.Message.ShouldBe("No Administrator access to 'acme'.");
        result.ShouldBeOfType<OkObjectResult>();
        log.Calls.ShouldBe([(Org, new AccessAuditQuery { Project = Project })]);
        access.Lookups.ShouldBe([Org, ProjectKey]);
    }

    // A user acting through an actor (the MCP server) reads the trail like the user.
    [Fact]
    public async Task DelegatedUser_ReadsTheTrailWithTheRoleOfTheUser()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, log, access) = Create(new Dictionary<string, AccountRole> { [Org] = AccountRole.Administrator });

        var result = await http.GetAccessAudit(TestRequests.DelegatedUser(), Org);

        result.ShouldBeOfType<OkObjectResult>();
        log.Calls.ShouldBe([(Org, new AccessAuditQuery())]);
        access.Lookups.ShouldBe([Org]);
    }

    private static (AccessAuditHttp Http, RecordingAccessAuditLog Log, AuditRoleAccess Access) Create(
        IReadOnlyDictionary<string, AccountRole>? roles = null,
        AccountRole fallback = AccountRole.None)
    {
        var log = new RecordingAccessAuditLog();
        var access = new AuditRoleAccess(roles ?? new Dictionary<string, AccountRole>(), roles is null ? AccountRole.Administrator : fallback);
        return (new AccessAuditHttp(access, log), log, access);
    }

    private static TestHttpRequestData Request(Action<NameValueCollection>? query = null)
    {
        var request = TestRequests.User();
        query?.Invoke(request.Query);
        return request;
    }

    private static void ShouldBeBadRequest(Exception exception, string errorCode)
    {
        var response = ErrorResponseMapping.ToActionResult(exception).ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        response.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(errorCode);
    }

    // Records each query and answers one canned page, or throws the refusal.
    private sealed class RecordingAccessAuditLog : IAccessAuditLog
    {
        public List<(string Organization, AccessAuditQuery Query)> Calls { get; } = [];

        public AccessAuditResponse Page { get; } = new()
        {
            Items =
            [
                new AccessAuditEntry
                {
                    Id = "aca.0000000002",
                    Time = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero),
                    Action = AccessAuditAction.MemberGranted,
                    Actor = AuditActor.Account(TestRequests.UserId, "Ada") with { Via = TestRequests.ActorId },
                    AccessPointKey = ProjectKey,
                    Target = AccessAuditTarget.Account("account-2", "Grace"),
                    NewValue = nameof(AccountRole.Reader),
                },
            ],
            Count = 1,
            ContinuationToken = "MTphY2EuMDAwMDAwMDAwMg",
        };

        public Exception? Refusal { get; set; }

        public Task<AccessAuditResponse> QueryAsync(string organization, AccessAuditQuery query, CancellationToken cancellationToken = default)
        {
            Calls.Add((organization, query));
            return Refusal is null ? Task.FromResult(Page) : Task.FromException<AccessAuditResponse>(Refusal);
        }
    }

    // The exact role of the user on each access point, the fallback on the others; records the keys looked up.
    private sealed class AuditRoleAccess(IReadOnlyDictionary<string, AccountRole> roles, AccountRole fallback) : IAccessService
    {
        public List<string> Lookups { get; } = [];

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
        {
            accountId.ShouldBe(TestRequests.UserId);
            Lookups.Add(accessPointKey);
            return Task.FromResult(roles.GetValueOrDefault(accessPointKey, fallback));
        }

        public Task<Account?> GetAccountAsync(string id) => throw new NotSupportedException();

        public Task<Account> CreateAccountAsync(AccountCreate model, AccessCaller caller) => throw new NotSupportedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotSupportedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotSupportedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model, AccessCaller caller) => throw new NotSupportedException();

        public Task<bool> GrantPermissionAsync(Permission model, AccessCaller caller) => throw new NotSupportedException();

        public Task<bool> RevokePermissionAsync(Permission model, AccessCaller caller) => throw new NotSupportedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotSupportedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, AccessCaller caller) => throw new NotSupportedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, AccessCaller caller) => throw new NotSupportedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role, AccessCaller caller, string? detail = null) => throw new NotSupportedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotSupportedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotSupportedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model, AccessCaller caller) => throw new NotSupportedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId, AccessCaller caller) => throw new NotSupportedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId, AccessCaller caller) => throw new NotSupportedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotSupportedException();
    }
}
