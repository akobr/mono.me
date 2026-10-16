using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller.Transferring;

public static class ViewTransferEntryPoint
{
    // The jobs need a store (Cosmos DB), an archive storage and a queue (Azure Storage) registered as well.
    public static IServiceCollection AddViewTransfers(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ViewTransferOptions>()
            .Bind(configuration.GetSection(ViewTransferOptions.SectionName))
            .Validate(
                options => options.RetentionHours is >= ViewTransferOptions.MinRetentionHours and <= ViewTransferOptions.MaxRetentionHours,
                $"{ViewTransferOptions.SectionName}:{nameof(ViewTransferOptions.RetentionHours)} must be between {ViewTransferOptions.MinRetentionHours} and {ViewTransferOptions.MaxRetentionHours}.")
            .Validate(
                options => options.MaxDocumentBytes is >= 1 and <= ViewTransferOptions.MaxAllowedDocumentBytes,
                $"{ViewTransferOptions.SectionName}:{nameof(ViewTransferOptions.MaxDocumentBytes)} must be between 1 and {ViewTransferOptions.MaxAllowedDocumentBytes}.")
            .Validate(
                options => options.MaxActiveTransfers >= 1
                           && options.MaxArchiveBytes >= 1024
                           && options.MaxExtractedBytes >= options.MaxDocumentBytes
                           && options.MaxArchiveEntries >= 1
                           && options.MaxReportedItems >= 0,
                $"{ViewTransferOptions.SectionName}: the limits must be positive, and {nameof(ViewTransferOptions.MaxExtractedBytes)} at least {nameof(ViewTransferOptions.MaxDocumentBytes)}.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IViewTransferService, ViewTransferService>();
        return services;
    }
}
