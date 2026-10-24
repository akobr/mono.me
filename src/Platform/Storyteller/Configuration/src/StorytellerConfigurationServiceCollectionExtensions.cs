using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Container;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

public static class StorytellerConfigurationServiceCollectionExtensions
{
    // The refreshers of the Storyteller sources in the container's IConfiguration, and the logging of the providers:
    // without this call (or options.LoggerFactory) they are silent.
    public static IServiceCollection AddStorytellerConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IStorytellerConfigurationRefresherProvider>(
            serviceProvider => new StorytellerConfigurationRefresherProvider(serviceProvider.GetRequiredService<IConfiguration>()));

        // The only refresher; none or several throw and point to the provider. Resolved again on each request, as the
        // provider walks the configuration again.
        services.TryAddTransient(
            serviceProvider => StorytellerConfigurationRefresherProvider.GetSingle(serviceProvider.GetRequiredService<IStorytellerConfigurationRefresherProvider>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StorytellerLoggingHandover>());
        return services;
    }

    // IStorytellerDocuments, a singleton for jobs that read configurations while they run. The options follow the
    // environment contract of the provider, are read and validated here, and throw StorytellerConfigurationException. Key
    // is optional: the default scope of annotation keys. The options of the provider alone (RefreshInterval, Optional,
    // CacheDirectory, CacheProtector and Placement) are ignored. An application that uses both passes the same configure
    // delegate to both. Logs to the container's ILoggerFactory unless options.LoggerFactory is set. The first registration
    // wins.
    public static IServiceCollection AddStorytellerDocuments(this IServiceCollection services, Action<StorytellerConfigurationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new StorytellerConfigurationOptions();
        configure?.Invoke(options);
        var settings = SettingsReader.ReadForDocuments(options);

        // By factory, so the container disposes it, and with it the HttpClient and the memory cache.
        services.TryAddSingleton<IStorytellerDocuments>(
            serviceProvider => new StorytellerDocuments(settings, serviceProvider.GetService<ILoggerFactory>()));

        return services;
    }
}
