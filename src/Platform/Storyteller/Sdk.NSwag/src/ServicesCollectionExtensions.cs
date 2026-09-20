using System;
using System.Net.Http;
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

        ConfigureHttpClient<IAccessApiClient, AccessApiClient>(services);
        ConfigureHttpClient<IAnnotationsApiClient, AnnotationsApiClient>(services);
        ConfigureHttpClient<IConfigurationApiClient, ConfigurationApiClient>(services);

        return services;
    }

    private static void ConfigureHttpClient<TInterface, TImplementation>(IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddHttpClient<TInterface, TImplementation>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetService<ISdkConfiguration>();
                var handler = new HttpClientHandler();

                if (config?.ClientCertificate is not null)
                {
                    handler.ClientCertificates.Add(config.ClientCertificate);
                }

                return handler;
            });
    }
}
