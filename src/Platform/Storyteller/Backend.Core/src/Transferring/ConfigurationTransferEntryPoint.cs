using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller.Transferring;

public static class ConfigurationTransferEntryPoint
{
    // The jobs need a store (Cosmos DB), an archive storage and a queue (Azure Storage) registered as well.
    public static IServiceCollection AddConfigurationTransfers(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ConfigurationTransferOptions>()
            .Bind(configuration.GetSection(ConfigurationTransferOptions.SectionName))
            .Validate(
                options => options.RetentionHours is >= ConfigurationTransferOptions.MinRetentionHours and <= ConfigurationTransferOptions.MaxRetentionHours,
                $"{ConfigurationTransferOptions.SectionName}:{nameof(ConfigurationTransferOptions.RetentionHours)} must be between {ConfigurationTransferOptions.MinRetentionHours} and {ConfigurationTransferOptions.MaxRetentionHours}.")
            .Validate(
                options => options.MaxDocumentBytes is >= 1 and <= ConfigurationTransferOptions.MaxAllowedDocumentBytes,
                $"{ConfigurationTransferOptions.SectionName}:{nameof(ConfigurationTransferOptions.MaxDocumentBytes)} must be between 1 and {ConfigurationTransferOptions.MaxAllowedDocumentBytes}.")
            .Validate(
                options => options.MaxActiveTransfers >= 1
                           && options.MaxArchiveBytes >= 1024
                           && options.MaxExtractedBytes >= options.MaxDocumentBytes
                           && options.MaxArchiveEntries >= 1
                           && options.MaxReportedItems >= 0,
                $"{ConfigurationTransferOptions.SectionName}: the limits must be positive, and {nameof(ConfigurationTransferOptions.MaxExtractedBytes)} at least {nameof(ConfigurationTransferOptions.MaxDocumentBytes)}.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConfigurationTransferService, ConfigurationTransferService>();
        return services;
    }
}
