using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class AzureStorageEntryPoint
{
    // The default connection of the Functions runtime, which the queue trigger of the jobs listens on.
    public const string DefaultConnectionName = "AzureWebJobsStorage";

    // Archives in the blob container and jobs in the queue "configuration-transfers" of one storage account.
    public static IServiceCollection AddAzureStorageConfigurationTransfers(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName = DefaultConnectionName)
    {
        services.AddSingleton(_ => AzureStorageConnection.FromConfiguration(configuration, connectionName));
        services.AddSingleton<IConfigurationArchiveStorage>(provider => new AzureBlobConfigurationArchiveStorage(
            provider.GetRequiredService<AzureStorageConnection>().GetBlobContainer(ConfigurationTransferNames.Container)));
        services.AddSingleton<IConfigurationTransferQueue>(provider => new AzureQueueConfigurationTransferQueue(
            provider.GetRequiredService<AzureStorageConnection>().GetQueue(ConfigurationTransferNames.Queue)));
        return services;
    }
}
