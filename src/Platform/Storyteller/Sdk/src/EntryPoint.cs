using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace ApiSdk;

public static class EntryPoint
{
    public static IServiceCollection AddStorytellerApiClient(this IServiceCollection services, Uri baseUrl)
    {
        // Register Kiota middleware handlers in DI
        services.AddKiotaHandlers();

        // Register auth provider however your app gets tokens
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();
        services.AddSingleton<IAuthenticationProvider>(sp =>
        {
            var accessTokenProvider = sp.GetRequiredService<IAccessTokenProvider>();
            return new BaseBearerTokenAuthenticationProvider(accessTokenProvider);
        });

        // Register typed HttpClient used by the adapter
        services.AddHttpClient<ApiClientFactory>(client =>
            {
                client.BaseAddress = baseUrl;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AttachKiotaHandlers();

        // Register generated client
        services.AddTransient<ApiClient>(sp => sp.GetRequiredService<ApiClientFactory>().Create());

        return services;
    }

    private static IServiceCollection AddKiotaHandlers(this IServiceCollection services)
    {
        var kiotaHandlers = KiotaClientFactory.GetDefaultHandlerActivatableTypes();

        foreach (var handler in kiotaHandlers)
        {
            services.AddTransient(handler);
        }

        return services;
    }

    private static IHttpClientBuilder AttachKiotaHandlers(this IHttpClientBuilder builder)
    {
        var kiotaHandlers = KiotaClientFactory.GetDefaultHandlerActivatableTypes();

        foreach (var handler in kiotaHandlers)
        {
            builder.AddHttpMessageHandler(sp => (DelegatingHandler)sp.GetRequiredService(handler));
        }

        return builder;
    }
}
