using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Hosting;
using _42.Platform.Storyteller.Sdk.Hosting.Catalog;
using _42.Platform.Storyteller.Sdk.Hosting.Container;
using _42.Platform.Storyteller.Sdk.Hosting.Executions;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// The namespace of every Microsoft service registration, so services.AddStoryteller(…) needs no using.
namespace Microsoft.Extensions.DependencyInjection;

public static class StorytellerHostingServiceCollectionExtensions
{
    // IExecutionContext, ISubscriptionCatalog, IStorytellerDocuments when it is not registered yet, and the provider's
    // refresher and logging (AddStorytellerConfiguration). The options are the provider's (STORYTELLER_* when unset),
    // read and validated here, and throw StorytellerConfigurationException; the options of the provider alone are
    // ignored, and Key is optional until who am I is used. Who am I, the catalog and the documents share one bound
    // credential. Logs to the container's ILoggerFactory unless options.LoggerFactory is set. The first registration
    // wins: a later call returns a builder over it without reading its options.
    public static IStorytellerHostingBuilder AddStoryteller(this IServiceCollection services, Action<StorytellerConfigurationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(HostingSettings)))
        {
            var options = new StorytellerConfigurationOptions();
            configure?.Invoke(options);
            AddHosting(services, new HostingSettings(SettingsReader.ReadForDocuments(options)));
        }

        services.AddStorytellerConfiguration();
        return new StorytellerHostingBuilder(services);
    }

    // By factories, so the container disposes what it built: the transport, and who am I with its background reads.
    private static void AddHosting(IServiceCollection services, HostingSettings hosting)
    {
        var settings = hosting.Settings;

        services.AddSingleton(hosting);
        services.TryAddSingleton(_ => new CatalogTransport(settings));
        services.TryAddSingleton(serviceProvider => new CatalogReader(settings, serviceProvider.GetRequiredService<CatalogTransport>()));

        services.AddOptions<StorytellerExecutionContextOptions>()
            .Validate(
                options => options.RefreshInterval >= TimeSpan.Zero || options.RefreshInterval == Timeout.InfiniteTimeSpan,
                $"{nameof(StorytellerExecutionContextOptions)}.{nameof(StorytellerExecutionContextOptions.RefreshInterval)} must be zero, positive or infinite.");

        services.TryAddSingleton<IExecutionContext>(serviceProvider => new ExecutionContextService(
            settings,
            serviceProvider.GetRequiredService<CatalogReader>(),
            serviceProvider.GetRequiredService<IOptions<StorytellerExecutionContextOptions>>().Value,
            serviceProvider.GetService<ILoggerFactory>()));

        services.TryAddSingleton<ISubscriptionCatalog>(serviceProvider => new SubscriptionCatalog(
            settings,
            serviceProvider.GetRequiredService<CatalogReader>(),
            serviceProvider.GetService<ILoggerFactory>()));

        // Over the same settings, so over the same credential; an application that registered its own keeps it.
        services.TryAddSingleton<IStorytellerDocuments>(
            serviceProvider => new StorytellerDocuments(settings, serviceProvider.GetService<ILoggerFactory>()));
    }
}
