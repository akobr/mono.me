using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Container;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
}
