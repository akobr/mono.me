using System.Reflection;
using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ViewsHttpTests
{
    [Fact]
    public async Task Machine_CannotCreateUpdateOrDiscoverViews()
    {
        var http = new ViewsHttp(Untouched<IViewService>(), Untouched<IAccessService>());

        await Should.ThrowAsync<AccessDeniedException>(() => http.CreateView(MachineRequest(), new ViewCreate { Name = "beta" }, "acme", "web"));
        await Should.ThrowAsync<AccessDeniedException>(() => http.UpdateView(MachineRequest(), new ViewUpdate(), "acme", "web", "beta"));
        await Should.ThrowAsync<AccessDeniedException>(() => http.GetViews(MachineRequest(), "acme", "web", discover: true));
    }

    private static TestHttpRequestData MachineRequest()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
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
}
