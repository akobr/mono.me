using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal static class FunctionTestDoubles
{
    public static ServiceProvider CreateServices(
        IBearerTokenValidator validator,
        UserAuthenticationOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(validator);
        services.AddSingleton(Options.Create(options ?? new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.EntraId,
            TenantId = "tenant",
            ClientId = "client-id",
        }));
        return services.BuildServiceProvider();
    }

    public static (TestFunctionContext Context, TestHttpRequestData Request) CreateRequest(
        IServiceProvider services,
        IDictionary<string, string>? headers = null)
    {
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers);
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        return (context, request);
    }

    public static TestFunctionContext CreateNonHttp(IServiceProvider services)
    {
        var context = new TestFunctionContext(services);

        // A missing feature falls through to the worker default, which reads FunctionDefinition.
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(null));
        return context;
    }
}
