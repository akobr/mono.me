using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class AzureStorageEntryPoint
{
    // The default connection of the Functions runtime, which the queue trigger of the jobs listens on.
    public const string DefaultConnectionName = "AzureWebJobsStorage";

    // Archives in the blob container and jobs in the queue "configuration-transfers" of one storage account.
    public static IServiceCollection AddAzureStorageViewTransfers(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName = DefaultConnectionName)
    {
        services.AddSingleton(_ => AzureStorageConnection.FromConfiguration(configuration, connectionName));
        services.AddSingleton<IViewArchiveStorage>(provider => new AzureBlobViewArchiveStorage(
            provider.GetRequiredService<AzureStorageConnection>().GetBlobContainer(ViewTransferNames.Container)));
        services.AddSingleton<IViewTransferQueue>(provider => new AzureQueueViewTransferQueue(
            provider.GetRequiredService<AzureStorageConnection>().GetQueue(ViewTransferNames.Queue)));
        return services;
    }
}
