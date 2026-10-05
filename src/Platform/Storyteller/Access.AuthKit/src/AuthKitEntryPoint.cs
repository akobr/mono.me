using System.Net;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Polly;
using Polly.Timeout;

namespace _42.Platform.Storyteller;

public static class AuthKitEntryPoint
{
    public static IServiceCollection AddAuthKitUserAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddUserAuthenticationOptions(configuration);
        services.TryAddSingleton<AuthKitClaimNormalizer>();
        services.AddSingleton<IBearerClaimsNormalizer>(provider => provider.GetRequiredService<AuthKitClaimNormalizer>());
        services.AddSingleton<IBearerTokenValidator, AuthKitBearerTokenValidator>();

        // Typed clients are transient, so the resolver is too.
        services.AddWorkOsManagementClient();
        services.AddTransient<IUserProfileResolver, AuthKitUserProfileResolver>();
        return services;
    }

    // AuthKit M2M applications for ClientCredentials machine access, with any user provider.
    // Needs Auth:AuthKit:ClientId, AuthKitDomain, MachineOrganizationId and ApiKey (checked at startup).
    public static IServiceCollection AddAuthKitMachineAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.EnsureNoOtherIdentityProviderMachineAccess("AuthKit");
        services.AddUserAuthenticationOptions(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UserAuthenticationOptions>, AuthKitMachineAccessOptionsValidator>());
        services.TryAddSingleton<AuthKitClaimNormalizer>();
        services.AddWorkOsManagementClient();

        services.AddTransient<AuthKitMachineAccessService>();
        services.AddTransient<IIdentityProviderMachineAccessService>(provider => provider.GetRequiredService<AuthKitMachineAccessService>());
        services.AddSingleton<IMachineTokenValidator, AuthKitMachineTokenValidator>();
        return services;
    }

    // 429 means WorkOS did not process the call, so any request may repeat it. Server errors and
    // transport failures are retried only for GET and DELETE: a repeated POST could create a second
    // application or secret.
    internal static bool ShouldRetry(HttpResponseMessage? response, Exception? exception, HttpMethod? method)
    {
        if (response?.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            return false;
        }

        return exception is HttpRequestException or TimeoutRejectedException
            || (response is not null && (int)response.StatusCode >= 500);
    }

    private static void AddWorkOsManagementClient(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(WorkOsManagementClient)))
        {
            return;
        }

        services.AddHttpClient<WorkOsManagementClient>()
            .AddResilienceHandler("workos-management", ConfigureResilience);
    }

    private static void ConfigureResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldRetryAfterHeader = true,
            ShouldHandle = arguments => ValueTask.FromResult(ShouldRetry(
                arguments.Outcome.Result,
                arguments.Outcome.Exception,
                arguments.Outcome.Result?.RequestMessage?.Method ?? arguments.Context.GetRequestMessage()?.Method)),
        });
    }
}
