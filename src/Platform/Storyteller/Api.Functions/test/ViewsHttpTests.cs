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
    [Fact]
    public async Task Machine_CannotCreateUpdateOrDiscoverViews()
    {
        var http = new ViewsHttp(Untouched<IViewService>(), Untouched<IAccessService>(), FunctionTestDoubles.ApiJsonOptions);

        await Should.ThrowAsync<AccessDeniedException>(() => http.CreateView(MachineRequest(FunctionTestDoubles.ToJsonBody(new ViewCreate { Name = "beta" })), "acme", "web"));
        await Should.ThrowAsync<AccessDeniedException>(() => http.UpdateView(MachineRequest(FunctionTestDoubles.ToJsonBody(new ViewUpdate())), "acme", "web", "beta"));
        await Should.ThrowAsync<AccessDeniedException>(() => http.GetViews(MachineRequest(), "acme", "web", discover: true));
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
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user-1"), new("scp", Scopes.Annotation.Write) };
        return request;
    }

    // A machine's request; a body goes as JSON, with its content type.
    private static TestHttpRequestData MachineRequest(string? body = null)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is null ? null : FunctionTestDoubles.JsonContentType();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("azp", "machine-1"), new("scp", $"{Scopes.Annotation.Read} {Scopes.Annotation.Write}") };
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";
        return request;
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

        public Task<IReadOnlyList<View>> GetViewsAsync(string organization, string project, bool discover) => throw new NotSupportedException();

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
