using System.Reflection;
using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ViewsHttpTests
{
    private const string ManagingViewsNeedsUser = "Managing views needs a user with the Administrator role.";

    // Debug builds (DEV_AUTH) skip the role check of users.
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    // Creating, updating and discovering views are for users only: a machine is refused in every build, before its
    // document is read, whatever its credentials carry.
    [Fact]
    public async Task Machine_CannotCreateUpdateOrDiscoverViews()
    {
        var http = new ViewsHttp(Untouched<IViewService>(), Untouched<IAccessService>(), FunctionTestDoubles.ApiJsonOptions);

        await ShouldNeedAUserAsync(() => http.CreateView(MachineRequest(FunctionTestDoubles.ToJsonBody(new ViewCreate { Name = "beta" })), "acme", "web"));
        await ShouldNeedAUserAsync(() => http.UpdateView(MachineRequest(FunctionTestDoubles.ToJsonBody(new ViewUpdate())), "acme", "web", "beta"));
        await ShouldNeedAUserAsync(() => http.GetViews(MachineRequest(), "acme", "web", discover: true));
    }

    // The views hold no annotation data, so either read scope of a machine lists them (spec 7.6): a machine created for
    // configurations only lists them too.
    [Theory]
    [InlineData(MachineScope.AnnotationRead)]
    [InlineData(MachineScope.ConfigurationRead)]
    public async Task GetViews_ByAMachineWithOneReadScope_ListsTheViews(MachineScope scope)
    {
        var views = new RecordingViews();
        var http = new ViewsHttp(views, new RoleAccess(AccountRole.None, scopes: [scope]), FunctionTestDoubles.ApiJsonOptions);

        var result = await http.GetViews(TestRequests.Machine([scope]), "acme", "web");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<View>>()!.ShouldHaveSingleItem().Name.ShouldBe("default");
        views.Calls.ShouldBe(["list:acme.web:False"]);
    }

    // The writes, the secrets and the management scopes do not stand in for a read scope.
    [Fact]
    public async Task GetViews_ByAMachineCreatedWithNeitherReadScope_IsAccessDenied()
    {
        var http = new ViewsHttp(Untouched<IViewService>(), new RoleAccess(AccountRole.None, scopes: WithoutTheReadScopes()), FunctionTestDoubles.ApiJsonOptions);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetViews(TestRequests.Machine(), "acme", "web"));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the machine was not created with.");
    }

    [Fact]
    public async Task GetViews_ByAMachineWhoseCredentialsCarryNeitherReadScope_IsAccessDenied()
    {
        var http = new ViewsHttp(Untouched<IViewService>(), new RoleAccess(AccountRole.None, scopes: MachineScopes.All), FunctionTestDoubles.ApiJsonOptions);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetViews(TestRequests.Machine(WithoutTheReadScopes()), "acme", "web"));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the credentials of the machine do not carry.");
    }

    // Each scope must be stored and carried: one read scope stored and the other one carried is neither.
    [Fact]
    public async Task GetViews_ByAMachineWithOneReadScopeStoredAndTheOtherCarried_IsAccessDenied()
    {
        var http = new ViewsHttp(Untouched<IViewService>(), new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead]), FunctionTestDoubles.ApiJsonOptions);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetViews(TestRequests.Machine([MachineScope.ConfigurationRead]), "acme", "web"));

        exception.Message.ShouldBe("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the credentials of the machine do not carry.");
    }

    // Users carry no data scope; the Reader role lists the views, the Administrator role discovers them too.
    [Theory]
    [InlineData(AccountRole.Reader, false)]
    [InlineData(AccountRole.Administrator, true)]
    public async Task GetViews_ByAUser_NeedsOnlyTheRole(AccountRole role, bool discover)
    {
        var views = new RecordingViews();
        var http = new ViewsHttp(views, new RoleAccess(role), FunctionTestDoubles.ApiJsonOptions);

        var result = await http.GetViews(TestRequests.User(), "acme", "web", discover);

        result.ShouldBeOfType<OkObjectResult>();
        views.Calls.ShouldBe([$"list:acme.web:{discover}"]);
    }

    [Fact]
    public async Task GetViews_WithDiscoveryByAReader_IsDeniedInRelease()
    {
        if (DevAuth)
        {
            return;
        }

        var http = new ViewsHttp(Untouched<IViewService>(), new RoleAccess(AccountRole.Reader), FunctionTestDoubles.ApiJsonOptions);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetViews(TestRequests.User(), "acme", "web", discover: true));

        exception.Message.ShouldBe("No Administrator access to 'acme.web'.");
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The name is required,
    // so {} is not a view either.
    [Theory]
    [InlineData("", "The view is missing.")]
    [InlineData("null", "The view is missing.")]
    [InlineData("[1, 2]", "The request body is not a view (at $).")]
    [InlineData("{}", "The request body is not a view (at $).")]
    public async Task CreateView_MissingOrInvalidBody_ReturnsBadRequestWithoutCallingTheService(string body, string message)
    {
        var views = new RecordingViews();
        var http = CreateHttp(views);

        var result = await http.CreateView(UserRequest(body), "acme", "web");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        views.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task CreateView_BodyWithoutAContentType_IsRead()
    {
        var views = new RecordingViews();
        var http = CreateHttp(views);
        var body = FunctionTestDoubles.ToJsonBody(new ViewCreate { Name = "beta", Description = "Beta testers" });

        var result = await http.CreateView(UserRequest(body, contentType: false), "acme", "web");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<View>().Name.ShouldBe("beta");
        views.Calls.ShouldBe(["create:acme.web"]);
        views.Created.ShouldBe(new ViewCreate { Name = "beta", Description = "Beta testers" });
    }

    [Fact]
    public async Task CreateView_PropertyNamesInAnyCase_AreRead()
    {
        var views = new RecordingViews();
        var http = CreateHttp(views);
        const string body = """{ "NAME": "beta", "description": "Beta testers", }""";

        var result = await http.CreateView(UserRequest(body), "acme", "web");

        result.ShouldBeOfType<OkObjectResult>();
        views.Created.ShouldBe(new ViewCreate { Name = "beta", Description = "Beta testers" });
    }

    // The body of an update is optional: without one, the description is cleared, as with {}.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task UpdateView_MissingOrEmptyBody_ClearsTheDescription(string body)
    {
        var views = new RecordingViews();
        var http = CreateHttp(views);

        var result = await http.UpdateView(UserRequest(body), "acme", "web", "beta");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<View>().Name.ShouldBe("beta");
        views.Calls.ShouldBe(["update:acme.web:beta"]);
        views.Updated.ShouldBe(new ViewUpdate());
    }

    [Fact]
    public async Task UpdateView_InvalidBody_ReturnsBadRequestWithoutCallingTheService()
    {
        var views = new RecordingViews();
        var http = CreateHttp(views);

        var result = await http.UpdateView(UserRequest("[1, 2]"), "acme", "web", "beta");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("The request body is not a view update (at $).");
        error.ErrorCode.ShouldBeNull();
        views.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateView_WithADescription_PassesIt()
    {
        var views = new RecordingViews();
        var http = CreateHttp(views);

        var result = await http.UpdateView(UserRequest("""{ "description": "Beta testers" }"""), "acme", "web", "beta");

        result.ShouldBeOfType<OkObjectResult>();
        views.Updated.ShouldBe(new ViewUpdate { Description = "Beta testers" });
    }

    private static ViewsHttp CreateHttp(RecordingViews views)
    {
        return new ViewsHttp(views, new RoleAccess(AccountRole.Administrator), FunctionTestDoubles.ApiJsonOptions);
    }

    // An administrator's request; the body goes as JSON, with its content type unless contentType is false.
    private static TestHttpRequestData UserRequest(string body, bool contentType = true)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user-1"), new("scp", Scopes.User.Impersonation) };
        return request;
    }

    // A machine's request whose credentials carry every machine scope and User.Impersonation; a body goes as JSON,
    // with its content type.
    private static TestHttpRequestData MachineRequest(string? body = null)
    {
        return TestRequests.MachineCarrying([Scopes.User.Impersonation, .. MachineScopes.ToScopeStrings(MachineScopes.All)], body: body);
    }

    private static List<MachineScope> WithoutTheReadScopes()
    {
        return [.. MachineScopes.All.Where(scope => scope is not MachineScope.AnnotationRead and not MachineScope.ConfigurationRead)];
    }

    private static async Task ShouldNeedAUserAsync(Func<Task> act)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(act);
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(ManagingViewsNeedsUser);
    }

    // The machine must be rejected before any service is reached.
    private static T Untouched<T>()
        where T : class
    {
        return DispatchProxy.Create<T, UntouchedProxy>();
    }

    public class UntouchedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
        }
    }

    // Records the models the handler passed and answers with the registered view.
    private sealed class RecordingViews : IViewService
    {
        public List<string> Calls { get; } = [];

        public ViewCreate? Created { get; private set; }

        public ViewUpdate? Updated { get; private set; }

        public Task<IReadOnlyList<View>> GetViewsAsync(string organization, string project, bool discover)
        {
            Calls.Add($"list:{organization}.{project}:{discover}");
            return Task.FromResult<IReadOnlyList<View>>([new View { Name = "default", IsDefault = true }]);
        }

        public Task<View> CreateViewAsync(string organization, string project, ViewCreate model, AuditActor actor)
        {
            Calls.Add($"create:{organization}.{project}");
            Created = model;
            return Task.FromResult(new View { Name = model.Name, Description = model.Description, IsRegistered = true });
        }

        public Task<View> UpdateViewAsync(string organization, string project, string view, ViewUpdate model, AuditActor actor)
        {
            Calls.Add($"update:{organization}.{project}:{view}");
            Updated = model;
            return Task.FromResult(new View { Name = view, Description = model.Description, IsRegistered = true });
        }
    }
}
