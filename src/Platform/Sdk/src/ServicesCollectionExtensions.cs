using System;
using _42.Platform.Sdk.Api;
using _42.Platform.Sdk.Client;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Sdk;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddPlatformSdk(this IServiceCollection services)
    {

        services.AddSingleton<IAccessApi, AccessApi>();
        services.AddSingleton<IAccessApiAsync>(p => p.GetRequiredService<IAccessApi>());
        services.AddSingleton<IAccessApiSync>(p => p.GetRequiredService<IAccessApi>());

        services.AddSingleton<IAnnotationsApi, AnnotationsApi>();
        services.AddSingleton<IAnnotationsApiAsync>(p => p.GetRequiredService<IAnnotationsApi>());
        services.AddSingleton<IAnnotationsApiSync>(p => p.GetRequiredService<IAnnotationsApi>());

        services.AddSingleton<IConfigurationApi, ConfigurationApi>();
        services.AddSingleton<IConfigurationApiAsync>(p => p.GetRequiredService<IConfigurationApi>());
        services.AddSingleton<IConfigurationApiSync>(p => p.GetRequiredService<IConfigurationApi>());

        return services;
    }
}
