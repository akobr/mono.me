using System.Text.Json;
using System.Text.Json.Serialization;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal static class FunctionTestDoubles
{
    // The JSON options Program.cs configures for the API, which the handlers read their bodies with.
    public static IOptions<JsonOptions> ApiJsonOptions { get; } = CreateApiJsonOptions();

    public static JsonSerializerOptions ApiJsonSerializerOptions => ApiJsonOptions.Value.JsonSerializerOptions;

    public static ServiceProvider CreateServices(
        IBearerTokenValidator validator,
        UserAuthenticationOptions? options = null,
        params IMachineTokenValidator[] machineValidators)
    {
        var resolved = Options.Create(options ?? new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.EntraId,
            TenantId = "tenant",
            ClientId = "client-id",
        });
        IBearerClaimsNormalizer normalizer = resolved.Value.Provider == IdentityProviderKind.AuthKit
            ? new AuthKitClaimNormalizer(resolved)
            : new EntraIdClaimNormalizer(resolved);

        var services = new ServiceCollection();
        services.AddSingleton(validator);
        services.AddSingleton(resolved);
        services.AddSingleton(normalizer);

        foreach (var machineValidator in machineValidators)
        {
            services.AddSingleton(machineValidator);
        }

        return services.BuildServiceProvider();
    }

    public static (TestFunctionContext Context, TestHttpRequestData Request) CreateRequest(
        IServiceProvider services,
        IDictionary<string, string>? headers = null,
        string method = "GET",
        string? body = null)
    {
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers, method, body);
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

    // The value as the API's JSON, the way a client sends it.
    public static string ToJsonBody<T>(T value)
    {
        return JsonSerializer.Serialize(value, ApiJsonSerializerOptions);
    }

    // The headers of a request with a JSON body: a new set each call, for a test to add its own.
    public static Dictionary<string, string> JsonContentType()
    {
        return new Dictionary<string, string> { ["Content-Type"] = "application/json" };
    }

    private static IOptions<JsonOptions> CreateApiJsonOptions()
    {
        var options = new JsonOptions();
        options.JsonSerializerOptions.PropertyNamingPolicy = new NoChangeNamingPolicy();
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new JObjectConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.AllowTrailingCommas = true;
        return Options.Create(options);
    }
}
