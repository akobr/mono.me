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
        => services.AddStorytellerSdk(configurationFactory, null);

    // configureHttpClient runs for each of the seven typed clients, after the primary handler is set,
    // for example to add a DelegatingHandler.
    public static IServiceCollection AddStorytellerSdk(
        this IServiceCollection services,
        Func<ISdkConfiguration>? configurationFactory,
        Action<IHttpClientBuilder>? configureHttpClient)
    {
        if (configurationFactory != null)
        {
            services.TryAddSingleton(_ => configurationFactory());
        }
        else
        {
            services.TryAddSingleton<ISdkConfiguration, SdkConfiguration>();
        }

        ConfigureHttpClient<IAccessApiClient, AccessApiClient>(services, configureHttpClient);
        ConfigureHttpClient<IAnnotationsApiClient, AnnotationsApiClient>(services, configureHttpClient);
        ConfigureHttpClient<IConfigurationsApiClient, ConfigurationsApiClient>(services, configureHttpClient);
        ConfigureHttpClient<ISchemasApiClient, SchemasApiClient>(services, configureHttpClient);
        ConfigureHttpClient<IKeyValuesApiClient, KeyValuesApiClient>(services, configureHttpClient);
        ConfigureHttpClient<ITemplatesApiClient, TemplatesApiClient>(services, configureHttpClient);
        ConfigureHttpClient<ITransfersApiClient, TransfersApiClient>(services, configureHttpClient);

        return services;
    }

    private static void ConfigureHttpClient<TInterface, TImplementation>(IServiceCollection services, Action<IHttpClientBuilder>? configureHttpClient)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        var builder = services.AddHttpClient<TInterface, TImplementation>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetService<ISdkConfiguration>();
                var handler = new HttpClientHandler();

                if (config?.ClientCertificate is not null)
                {
                    if (!config.ClientCertificate.HasPrivateKey)
                    {
                        throw new InvalidOperationException(
                            "The configured client certificate does not have a private key. " +
                            "mTLS requires a certificate with a private key.");
                    }

                    handler.ClientCertificates.Add(config.ClientCertificate);
                }

                return handler;
            });

        configureHttpClient?.Invoke(builder);
    }
}
