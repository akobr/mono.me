using System.Collections.Specialized;
using System.Net;
using System.Reflection;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Notifying;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The three routes of the inbox of a view (spec "Inbox of a View", 5): users only, the Contributor role on the project,
// and the role of the caller handed to the service, which hides the items above it. Debug builds (DEV_AUTH) skip the
// role checks of users unless a test enforces them (UserRoleChecks), and the caller then counts as Owner; the access
// double gives an Owner unless a test names the role, so the other tests hand over the same role in every build.
public class ViewInboxHttpTests
{
    private const string Org = "acme";
    private const string Project = "web";
    private const string View = "default";
    private const string ProjectKey = "acme.web";
    private const string NoContributor = "No Contributor access to 'acme.web'.";

    // An id as the inbox issues it: the time of the item, then 32 hex characters.
    private const string ItemId = "202611220915000000000" + "0123456789abcdef0123456789abcdef";

    // The user of TestRequests.User, as the routes stamp an acceptance or a dismissal.
    private static readonly AuditActor Caller = AuditActor.Account(TestRequests.UserId, "Ada");

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    // The role of the project is looked up once per request and handed over as it is: the service lists, accepts and
    // dismisses for a Contributor what a Contributor may see, and for an Owner everything.
    [Theory]
    [InlineData(AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets)]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public async Task EveryRoute_HandsTheRoleOfTheCallerToTheService_AndAnswersWhatItGives(AccountRole role)
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, inbox, access) = Create(role);

        var list = await http.GetViewInbox(Request(), Org, Project, View);
        var accepted = await http.AcceptViewInboxItem(Request("POST"), Org, Project, View, ItemId);
        var dismissed = await http.DismissViewInboxItem(Request("POST"), Org, Project, View, ItemId);

        list.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(inbox.Page);
        accepted.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(inbox.Accepted);
        dismissed.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(inbox.Dismissed);
        inbox.Lists.ShouldBe([new ListCall(Org, Project, View, new ViewInboxQuery { Role = role })]);
        inbox.Accepts.ShouldBe([new ItemCall(Org, Project, View, ItemId, role, Caller)]);
        inbox.Dismissals.ShouldBe([new ItemCall(Org, Project, View, ItemId, role, Caller)]);
        access.Lookups.ShouldBe([(TestRequests.UserId, ProjectKey), (TestRequests.UserId, ProjectKey), (TestRequests.UserId, ProjectKey)]);
    }

    // Below Contributor the inbox is closed: 403 AccessDenied with the role it needs, before the service is asked, so a
    // Reader learns nothing about an item, not even whether its id exists.
    [Theory]
    [InlineData(AccountRole.None)]
    [InlineData(AccountRole.Reader)]
    public async Task EveryRoute_ByAUserBelowContributor_IsForbidden(AccountRole role)
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, inbox, access) = Create(role);

        foreach (var (route, act, method) in Routes(http))
        {
            var denied = await Should.ThrowAsync<AccessDeniedException>(() => act(Request(method)));
            denied.Message.ShouldBe(NoContributor, route);

            var response = ErrorResponseMapping.ToActionResult(denied).ShouldBeOfType<ObjectResult>();
            response.StatusCode.ShouldBe((int)HttpStatusCode.Forbidden, route);
            response.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.AccessDenied, route);
        }

        inbox.Calls.ShouldBe(0);
        access.Lookups.Count.ShouldBe(3, "the role is looked up, and found too low, once per request");
    }

    // Users only: a machine is refused in every build, with or without the role checks, also one whose credentials carry
    // every scope and User.Impersonation and whose account would be an Owner, before a role is looked up or the service
    // is asked. No machine scope gives the inbox.
    [Fact]
    public async Task Machine_IsAccessDeniedOnEveryRoute_InEveryBuild()
    {
        var (http, inbox, access) = Create(AccountRole.Owner);

        foreach (var (route, act, method) in Routes(http))
        {
            await UserOnlyRoutes.ShouldRefuseMachineAsync(() => act(UserOnlyRoutes.Machine(method)));
            await UserOnlyRoutes.ShouldRefuseMachineAsync(() => act(TestRequests.Machine(method: method)));

            using (UserRoleChecks.Enforce())
            {
                await UserOnlyRoutes.ShouldRefuseMachineAsync(() => act(UserOnlyRoutes.Machine(method)));
            }

            var denied = await Should.ThrowAsync<AccessDeniedException>(() => act(TestRequests.Machine(method: method)));
            var response = ErrorResponseMapping.ToActionResult(denied).ShouldBeOfType<ObjectResult>();
            response.StatusCode.ShouldBe((int)HttpStatusCode.Forbidden, route);
            response.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.AccessDenied, route);
        }

        inbox.Calls.ShouldBe(0);
        access.Lookups.ShouldBeEmpty();
    }

    // Data scopes in a user token do not stand in for User.Impersonation: 401 in Release builds, before a role is looked
    // up. Debug builds (DEV_AUTH) skip the check and serve the request.
    [Fact]
    public async Task TokenThatDoesNotActForAUser_IsUnauthorized()
    {
        var (http, inbox, access) = Create();

        if (DevAuth)
        {
            foreach (var (route, act, method) in Routes(http))
            {
                (await act(UserOnlyRoutes.NotActingForAUser(method))).ShouldBeOfType<OkObjectResult>(route);
            }

            return;
        }

        foreach (var (route, act, method) in Routes(http))
        {
            var exception = await Should.ThrowAsync<SecurityTokenException>(() => act(UserOnlyRoutes.NotActingForAUser(method)));
            exception.Message.ShouldBe(UserOnlyRoutes.NotActingForAUserMessage, route);
        }

        inbox.Calls.ShouldBe(0);
        access.Lookups.ShouldBeEmpty();
    }

    // Debug builds (DEV_AUTH) skip the role checks of users: no role is looked up and the caller counts as Owner, so the
    // service hides nothing, also from an account that is only a Reader. A Release build always checks.
    [Fact]
    public async Task WithoutTheRoleChecks_TheCallerCountsAsOwner_AndNoRoleIsLookedUp()
    {
        var (http, inbox, access) = Create(AccountRole.Reader);

        if (!DevAuth)
        {
            foreach (var (route, act, method) in Routes(http))
            {
                var denied = await Should.ThrowAsync<AccessDeniedException>(() => act(Request(method)));
                denied.Message.ShouldBe(NoContributor, route);
            }

            inbox.Calls.ShouldBe(0);
            access.Lookups.Count.ShouldBe(3);
            return;
        }

        foreach (var (route, act, method) in Routes(http))
        {
            (await act(Request(method))).ShouldBeOfType<OkObjectResult>(route);
        }

        inbox.Lists.ShouldBe([new ListCall(Org, Project, View, new ViewInboxQuery { Role = AccountRole.Owner })]);
        inbox.Accepts.ShouldBe([new ItemCall(Org, Project, View, ItemId, AccountRole.Owner, Caller)]);
        inbox.Dismissals.ShouldBe([new ItemCall(Org, Project, View, ItemId, AccountRole.Owner, Caller)]);
        access.Lookups.ShouldBeEmpty();
    }

    // Debug builds (DEV_AUTH) also serve a request without an identity: whether the role check is skipped is asked before
    // the identity is read, so the request is an Owner's and is stamped System. A Release build answers 401.
    [Fact]
    public async Task RequestWithoutAnIdentity_IsAnOwnersInADebugBuild_AndUnauthorizedInAReleaseBuild()
    {
        var (http, inbox, access) = Create(AccountRole.Reader);

        if (!DevAuth)
        {
            foreach (var (route, act, method) in Routes(http))
            {
                var exception = await Should.ThrowAsync<SecurityTokenException>(() => act(Anonymous(method)));
                exception.Message.ShouldBe(UserOnlyRoutes.NotActingForAUserMessage, route);
            }

            inbox.Calls.ShouldBe(0);
            access.Lookups.ShouldBeEmpty();
            return;
        }

        foreach (var (route, act, method) in Routes(http))
        {
            (await act(Anonymous(method))).ShouldBeOfType<OkObjectResult>(route);
        }

        inbox.Lists.ShouldBe([new ListCall(Org, Project, View, new ViewInboxQuery { Role = AccountRole.Owner })]);
        inbox.Accepts.ShouldBe([new ItemCall(Org, Project, View, ItemId, AccountRole.Owner, AuditActor.System)]);
        inbox.Dismissals.ShouldBe([new ItemCall(Org, Project, View, ItemId, AccountRole.Owner, AuditActor.System)]);
        access.Lookups.ShouldBeEmpty();
    }

    // Where the role is checked, it is the role of somebody: a request without an identity is 401 in every build, never
    // an Owner's, and nothing is looked up for it.
    [Fact]
    public async Task RequestWithoutAnIdentity_WithTheRoleChecks_IsUnauthorized()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, inbox, access) = Create(AccountRole.Owner);

        foreach (var (_, act, method) in Routes(http))
        {
            await Should.ThrowAsync<SecurityTokenException>(() => act(Anonymous(method)));
        }

        inbox.Calls.ShouldBe(0);
        access.Lookups.ShouldBeEmpty();
    }

    // The state is read as KeyValueQuery reads an enum: without it the open items, and a name in any casing.
    [Theory]
    [InlineData(null, ViewInboxFilter.Open)]
    [InlineData("", ViewInboxFilter.Open)]
    [InlineData("Open", ViewInboxFilter.Open)]
    [InlineData("open", ViewInboxFilter.Open)]
    [InlineData("OPEN", ViewInboxFilter.Open)]
    [InlineData("Done", ViewInboxFilter.Done)]
    [InlineData("done", ViewInboxFilter.Done)]
    [InlineData("DONE", ViewInboxFilter.Done)]
    public async Task List_State_IsOpenByDefault_AndReadInAnyCase(string? state, ViewInboxFilter expected)
    {
        var (http, inbox, _) = Create();

        var result = await http.GetViewInbox(Request(query: q => q["state"] = state), Org, Project, View);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(inbox.Page);
        inbox.Lists.ShouldBe([new ListCall(Org, Project, View, new ViewInboxQuery { Role = AccountRole.Owner, Filter = expected })]);
    }

    // The states of an item are not states of the list, and two names are not one: 400 InvalidQuery, and no read.
    [Theory]
    [InlineData("Accepted")]
    [InlineData("Dismissed")]
    [InlineData("Closed")]
    [InlineData("All")]
    [InlineData("Open,Done")]
    [InlineData("Open Done")]
    [InlineData("2")]
    public async Task List_State_Unknown_IsInvalidQuery_AndReadsNoInbox(string state)
    {
        var (http, inbox, _) = Create();

        var exception = await Should.ThrowAsync<InvalidInputException>(() => http.GetViewInbox(Request(query: q => q["state"] = state), Org, Project, View));

        exception.Message.ShouldBe("The query parameter 'state' must be Open, Done.");
        ShouldAnswer(exception, HttpStatusCode.BadRequest, ErrorCodes.InvalidQuery);
        inbox.Calls.ShouldBe(0);
    }

    // The token goes to the service as it came, which decodes it; without one the service gets none.
    [Fact]
    public async Task List_ContinuationToken_PassesThrough()
    {
        var (http, inbox, _) = Create();

        await http.GetViewInbox(Request(), Org, Project, View);
        await http.GetViewInbox(
            Request(query: q =>
            {
                q["state"] = "Done";
                q["continuationToken"] = "MTp0aGUtbGFzdC1pZA";
            }),
            Org,
            Project,
            View);

        inbox.Lists.ShouldBe(
        [
            new ListCall(Org, Project, View, new ViewInboxQuery { Role = AccountRole.Owner }),
            new ListCall(Org, Project, View, new ViewInboxQuery { Role = AccountRole.Owner, Filter = ViewInboxFilter.Done, ContinuationToken = "MTp0aGUtbGFzdC1pZA" }),
        ]);
    }

    // The actor of an acceptance and of a dismissal is the audit actor of the request: the account with its name, and
    // Via when an actor (the MCP server) acts for the person. The role is the person's either way.
    [Fact]
    public async Task AcceptAndDismiss_StampTheCaller_WithViaForADelegatedUser()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, inbox, _) = Create(AccountRole.Administrator);
        var delegated = Caller with { Via = TestRequests.ActorId };

        await http.AcceptViewInboxItem(Request("POST"), Org, Project, View, ItemId);
        await http.AcceptViewInboxItem(TestRequests.DelegatedUser(method: "POST"), Org, Project, View, ItemId);
        await http.DismissViewInboxItem(Request("POST"), Org, Project, View, ItemId);
        await http.DismissViewInboxItem(TestRequests.DelegatedUser(method: "POST"), Org, Project, View, ItemId);

        ItemCall[] expected =
        [
            new ItemCall(Org, Project, View, ItemId, AccountRole.Administrator, Caller),
            new ItemCall(Org, Project, View, ItemId, AccountRole.Administrator, delegated),
        ];
        inbox.Accepts.ShouldBe(expected);
        inbox.Dismissals.ShouldBe(expected);
    }

    // A user acting through an actor reads the inbox like the user.
    [Fact]
    public async Task List_ByADelegatedUser_HasTheRoleOfTheUser()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var (http, inbox, access) = Create(AccountRole.Contributor);

        var result = await http.GetViewInbox(TestRequests.DelegatedUser(), Org, Project, View);

        result.ShouldBeOfType<OkObjectResult>();
        inbox.Lists.ShouldBe([new ListCall(Org, Project, View, new ViewInboxQuery { Role = AccountRole.Contributor })]);
        access.Lookups.ShouldBe([(TestRequests.UserId, ProjectKey)]);
    }

    // The token of the invocation, so a caller that goes away stops the read and an acceptance before its claim.
    [Fact]
    public async Task EveryRoute_HandsTheCancellationTokenOfTheInvocationOn()
    {
        using var cancellation = new CancellationTokenSource();
        var (http, inbox, _) = Create();

        foreach (var (route, act, method) in Routes(http))
        {
            var request = Request(method);
            ((TestFunctionContext)request.FunctionContext).Token = cancellation.Token;

            (await act(request)).ShouldBeOfType<OkObjectResult>(route);
        }

        inbox.Tokens.ShouldBe([cancellation.Token, cancellation.Token, cancellation.Token]);
        inbox.Tokens.ShouldNotContain(CancellationToken.None);
    }

    // No request body: whatever a client sends is not read, so it cannot fail the request.
    [Fact]
    public async Task AcceptAndDismiss_ReadNoRequestBody()
    {
        var (http, inbox, _) = Create();
        var accept = Request("POST", body: "{ \"State\": ");
        var dismiss = Request("POST", body: "not json");

        (await http.AcceptViewInboxItem(accept, Org, Project, View, ItemId)).ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(inbox.Accepted);
        (await http.DismissViewInboxItem(dismiss, Org, Project, View, ItemId)).ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(inbox.Dismissed);

        accept.Body.Position.ShouldBe(0);
        dismiss.Body.Position.ShouldBe(0);
    }

    // No step-up: the routes change no access and reveal no secret, so a sign-in that is not recent (Auth:StepUp) is
    // served as a recent one is, in every build, also where the role is checked: a Debug build without the role checks
    // returns before the role is looked up, and a step-up belongs after the role.
    [Fact]
    public async Task EveryRoute_WithAStaleSignIn_NeedsNoStepUp()
    {
        var (http, inbox, _) = Create();

        foreach (var (route, act, method) in Routes(http))
        {
            (await act(Request(method).WithSignIn(TestRequests.StaleSignIn))).ShouldBeOfType<OkObjectResult>(route);

            using (UserRoleChecks.Enforce())
            {
                (await act(Request(method).WithSignIn(TestRequests.StaleSignIn))).ShouldBeOfType<OkObjectResult>(route);
            }
        }

        inbox.Calls.ShouldBe(6);
    }

    // The routes catch nothing: what the service refuses reaches the middleware as it was thrown, and the middleware
    // answers it by its type, with the code of the service. The handler's own refusal of an accepted action
    // (TransferLimit) arrives the same way.
    [Theory]
    [InlineData(nameof(InvalidInputException), ErrorCodes.InvalidQuery, HttpStatusCode.BadRequest)]
    [InlineData(nameof(NotFoundException), ErrorCodes.NotFound, HttpStatusCode.NotFound)]
    [InlineData(nameof(ConflictException), ErrorCodes.InboxItemNotAction, HttpStatusCode.Conflict)]
    [InlineData(nameof(ConflictException), ErrorCodes.InboxItemClosed, HttpStatusCode.Conflict)]
    [InlineData(nameof(ConflictException), ErrorCodes.Conflict, HttpStatusCode.Conflict)]
    [InlineData(nameof(ConflictException), ErrorCodes.TransferLimit, HttpStatusCode.Conflict)]
    [InlineData(nameof(FeatureNotConfiguredException), ErrorCodes.InboxActionNotAvailable, HttpStatusCode.NotImplemented)]
    public async Task RefusalOfTheService_GoesOutAsItIs(string type, string errorCode, HttpStatusCode status)
    {
        var (http, inbox, _) = Create();
        StorytellerException refusal = type switch
        {
            nameof(InvalidInputException) => new InvalidInputException("refused", errorCode),
            nameof(NotFoundException) => new NotFoundException("refused", errorCode),
            nameof(ConflictException) => new ConflictException("refused", errorCode),
            _ => new FeatureNotConfiguredException("refused", errorCode),
        };
        inbox.Refusal = refusal;

        foreach (var (route, act, method) in Routes(http))
        {
            var thrown = await Should.ThrowAsync<StorytellerException>(() => act(Request(method)));
            thrown.ShouldBeSameAs(refusal, route);
        }

        inbox.Calls.ShouldBe(3);
        ShouldAnswer(refusal, status, errorCode);
    }

    [Fact]
    public void Routes_AreBelowTheViewUnderTheInboxTag()
    {
        var operations = typeof(ViewInboxHttp)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => (
                Function: method.GetCustomAttribute<FunctionAttribute>()!.Name,
                Operation: method.GetCustomAttribute<OpenApiOperationAttribute>()!,
                Trigger: method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()).Single(trigger => trigger is not null)!))
            .Select(operation => $"{operation.Trigger.Methods!.Single().ToUpperInvariant()} {operation.Trigger.Route} {operation.Function} {operation.Operation.OperationId} {string.Join(',', operation.Operation.Tags)}")
            .ToList();

        operations.ShouldBe(
            [
                "GET v1/{organization}/{project}/{view}/inbox GetViewInbox GetViewInbox Inbox",
                "POST v1/{organization}/{project}/{view}/inbox/{id}/accept AcceptViewInboxItem AcceptViewInboxItem Inbox",
                "POST v1/{organization}/{project}/{view}/inbox/{id}/dismiss DismissViewInboxItem DismissViewInboxItem Inbox",
            ],
            ignoreOrder: true);
    }

    // Every user has the given role on every access point: an Owner unless the test names another.
    private static (ViewInboxHttp Http, RecordingInbox Inbox, RecordingRoleAccess Access) Create(AccountRole role = AccountRole.Owner)
    {
        var inbox = new RecordingInbox();
        var access = new RecordingRoleAccess(role);
        return (new ViewInboxHttp(inbox, access), inbox, access);
    }

    // The three routes, each with the method of its request.
    private static (string Route, Func<TestHttpRequestData, Task<IActionResult>> Act, string Method)[] Routes(ViewInboxHttp http)
    {
        return
        [
            (nameof(ViewInboxHttp.GetViewInbox), request => http.GetViewInbox(request, Org, Project, View), "GET"),
            (nameof(ViewInboxHttp.AcceptViewInboxItem), request => http.AcceptViewInboxItem(request, Org, Project, View, ItemId), "POST"),
            (nameof(ViewInboxHttp.DismissViewInboxItem), request => http.DismissViewInboxItem(request, Org, Project, View, ItemId), "POST"),
        ];
    }

    private static TestHttpRequestData Request(string method = "GET", Action<NameValueCollection>? query = null, string? body = null)
    {
        var request = TestRequests.User(method, body);
        query?.Invoke(request.Query);
        return request;
    }

    // A request as one without credentials leaves the authentication middlewares: no claims, no machine.
    private static TestHttpRequestData Anonymous(string method)
    {
        return FunctionTestDoubles.CreateRequest(new ServiceCollection().BuildServiceProvider(), method: method).Request;
    }

    private static void ShouldAnswer(Exception exception, HttpStatusCode status, string errorCode)
    {
        var response = ErrorResponseMapping.ToActionResult(exception).ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe((int)status);
        response.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(errorCode);
    }

    private static ViewInboxItem Item(ViewInboxItemState state)
    {
        return new ViewInboxItem
        {
            Id = ItemId,
            Kind = ViewInboxItemKind.Action,
            Topic = "SchemaSuggestion",
            Title = "Generate a schema for 4 similar configurations",
            MinimalRole = AccountRole.Contributor,
            State = state,
            CreatedAt = new DateTimeOffset(2026, 11, 22, 9, 15, 0, TimeSpan.Zero),
            CreatedBy = AuditActor.System,
            ExpiresAt = new DateTimeOffset(2026, 12, 22, 9, 15, 0, TimeSpan.Zero),
            Closed = state == ViewInboxItemState.Open ? null : AuditStamp.Of(Caller, new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero)),
            Action = "GenerateSchema",
        };
    }

    private sealed record ListCall(string Organization, string Project, string View, ViewInboxQuery Query);

    private sealed record ItemCall(string Organization, string Project, string View, string Id, AccountRole Role, AuditActor Actor);

    // Records what each route handed over, with the token of the call, and answers one canned page or item, or throws
    // the refusal.
    private sealed class RecordingInbox : IViewInboxService
    {
        public List<ListCall> Lists { get; } = [];

        public List<ItemCall> Accepts { get; } = [];

        public List<ItemCall> Dismissals { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int Calls => Lists.Count + Accepts.Count + Dismissals.Count;

        public ViewInboxResponse Page { get; } = new()
        {
            Items = [Item(ViewInboxItemState.Open)],
            Count = 1,
            OpenCount = 3,
            ContinuationToken = "MTp0aGUtbGFzdC1pZA",
        };

        public ViewInboxItem Accepted { get; } = Item(ViewInboxItemState.Accepted);

        public ViewInboxItem Dismissed { get; } = Item(ViewInboxItemState.Dismissed);

        public Exception? Refusal { get; set; }

        public Task<ViewInboxResponse> GetItemsAsync(string organization, string project, string view, ViewInboxQuery query, CancellationToken cancellationToken = default)
        {
            Lists.Add(new ListCall(organization, project, view, query));
            Tokens.Add(cancellationToken);
            return Answer(Page);
        }

        public Task<ViewInboxItem> AcceptAsync(string organization, string project, string view, string id, AccountRole role, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Accepts.Add(new ItemCall(organization, project, view, id, role, actor));
            Tokens.Add(cancellationToken);
            return Answer(Accepted);
        }

        public Task<ViewInboxItem> DismissAsync(string organization, string project, string view, string id, AccountRole role, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Dismissals.Add(new ItemCall(organization, project, view, id, role, actor));
            Tokens.Add(cancellationToken);
            return Answer(Dismissed);
        }

        private Task<T> Answer<T>(T value)
        {
            return Refusal is null ? Task.FromResult(value) : Task.FromException<T>(Refusal);
        }
    }

    // Every account has the role on every access point; records who was looked up, and where.
    private sealed class RecordingRoleAccess(AccountRole granted) : IAccessService
    {
        public List<(string AccountId, string AccessPointKey)> Lookups { get; } = [];

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
        {
            Lookups.Add((accountId, accessPointKey));
            return Task.FromResult(granted);
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
