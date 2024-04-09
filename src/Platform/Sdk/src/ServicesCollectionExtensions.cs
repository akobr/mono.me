using System;
using _42.Platform.Sdk.Api;
using _42.Platform.Sdk.Client;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Sdk;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddPlatformSdk(
        this IServiceCollection services,
        Func<IReadableConfiguration>? configurationFactory = null,
        ExceptionFactory? exceptionFactory = null)
    {
        configurationFactory ??= () => new SdkConfiguration();

        services.AddSingleton<IConfigurationFactory>(_ => new ConfigurationFactory(configurationFactory));
        services.AddSingleton<IExceptionFactoryProvider>(_ => new ExceptionFactoryProvider(exceptionFactory));
        services.AddSingleton<IRestClientFactory, RestClientFactory>();
        services.AddSingleton<IApiFactory, ApiFactory>();

        services.AddSingleton<IAnnotationsApi>(p => p.GetRequiredService<IApiFactory>().GetAnnotationsApi());
        services.AddSingleton<IAnnotationsApiAsync>(p => p.GetRequiredService<IAnnotationsApi>());
        services.AddSingleton<IAnnotationsApiSync>(p => p.GetRequiredService<IAnnotationsApi>());

        services.AddSingleton<IConfigurationApi>(p => p.GetRequiredService<IApiFactory>().GetConfigurationApi());
        services.AddSingleton<IConfigurationApiAsync>(p => p.GetRequiredService<IConfigurationApi>());
        services.AddSingleton<IConfigurationApiSync>(p => p.GetRequiredService<IConfigurationApi>());

        services.AddSingleton<IAccessApi>(p => p.GetRequiredService<IApiFactory>().GetAccessApi());
        services.AddSingleton<IAccessApiAsync>(p => p.GetRequiredService<IAccessApi>());
        services.AddSingleton<IAccessApiSync>(p => p.GetRequiredService<IAccessApi>());

        return services;
    }
}
