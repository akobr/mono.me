using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller.Sdk;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddStorytellerSdk(
        this IServiceCollection services,
        Func<ISdkConfiguration>? configurationFactory = null)
    {
        if (configurationFactory != null)
        {
            services.TryAddSingleton(_ => configurationFactory());
        }
        else
        {
            services.TryAddSingleton<ISdkConfiguration, SdkConfiguration>();
        }

        services.AddHttpClient<IAccessApiClient, AccessApiClient>();
        services.AddHttpClient<IAnnotationsApiClient, AnnotationsApiClient>();
        services.AddHttpClient<IConfigurationApiClient, ConfigurationApiClient>();

        return services;
    }
}
