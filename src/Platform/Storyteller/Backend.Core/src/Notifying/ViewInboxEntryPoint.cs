using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller.Notifying;

public static class ViewInboxEntryPoint
{
    // The inbox needs a store (Cosmos DB) registered as well. What an accepted action runs is a registered
    // IViewInboxActionHandler; only the service takes the handlers, so a producer never depends on them.
    public static IServiceCollection AddViewInbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ViewInboxOptions>()
            .Bind(configuration.GetSection(ViewInboxOptions.SectionName))
            .Validate(
                options => options.RetentionDays is >= ViewInboxOptions.MinRetentionDays and <= ViewInboxOptions.MaxRetentionDays,
                $"{ViewInboxOptions.SectionName}:{nameof(ViewInboxOptions.RetentionDays)} must be between {ViewInboxOptions.MinRetentionDays} and {ViewInboxOptions.MaxRetentionDays}.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IViewInboxPublisher, ViewInboxPublisher>();
        services.TryAddSingleton<IViewInboxService, ViewInboxService>();
        return services;
    }
}
