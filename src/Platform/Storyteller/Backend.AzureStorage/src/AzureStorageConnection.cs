using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Transferring;

// A storage account configured like a connection of the Functions runtime: a connection string as the value, or
// an identity-based section with accountName, or blobServiceUri and queueServiceUri (and clientId for a
// user-assigned identity).
public sealed class AzureStorageConnection
{
    private readonly string? _connectionString;
    private readonly Uri? _blobServiceUri;
    private readonly Uri? _queueServiceUri;
    private readonly TokenCredential? _credential;

    private AzureStorageConnection(string connectionString)
    {
        _connectionString = connectionString;
    }

    private AzureStorageConnection(Uri blobServiceUri, Uri queueServiceUri, TokenCredential credential)
    {
        _blobServiceUri = blobServiceUri;
        _queueServiceUri = queueServiceUri;
        _credential = credential;
    }

    public static AzureStorageConnection FromConfiguration(IConfiguration configuration, string name)
    {
        var section = configuration.GetSection(name);
        var connectionString = section.Value ?? configuration.GetConnectionString(name);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new AzureStorageConnection(connectionString);
        }

        var accountName = section["accountName"];
        var blobServiceUri = section["blobServiceUri"] ?? (accountName is null ? null : $"https://{accountName}.blob.core.windows.net");
        var queueServiceUri = section["queueServiceUri"] ?? (accountName is null ? null : $"https://{accountName}.queue.core.windows.net");

        if (blobServiceUri is null || queueServiceUri is null)
        {
            throw new InvalidOperationException(
                $"The storage connection '{name}' is not configured: set a connection string, accountName, or blobServiceUri and queueServiceUri.");
        }

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = section["clientId"] });
        return new AzureStorageConnection(new Uri(blobServiceUri), new Uri(queueServiceUri), credential);
    }

    public BlobContainerClient GetBlobContainer(string containerName)
    {
        var service = _connectionString is not null
            ? new BlobServiceClient(_connectionString)
            : new BlobServiceClient(_blobServiceUri!, _credential!);

        return service.GetBlobContainerClient(containerName);
    }

    // Base64 messages, the encoding the queue trigger of the Functions runtime expects.
    public QueueClient GetQueue(string queueName)
    {
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        var service = _connectionString is not null
            ? new QueueServiceClient(_connectionString, options)
            : new QueueServiceClient(_queueServiceUri!, _credential!, options);

        return service.GetQueueClient(queueName);
    }
}
