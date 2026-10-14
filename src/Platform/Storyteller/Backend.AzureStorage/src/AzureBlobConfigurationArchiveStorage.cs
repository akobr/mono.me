using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace _42.Platform.Storyteller.Transferring;

// Archives as block blobs of one container, created on first use.
public sealed class AzureBlobConfigurationArchiveStorage : IConfigurationArchiveStorage
{
    private const string ZipContentType = "application/zip";

    private readonly BlobContainerClient _container;
    private volatile bool _isContainerReady;

    public AzureBlobConfigurationArchiveStorage(BlobContainerClient container)
    {
        _container = container;
    }

    public async Task UploadAsync(string name, Stream content, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.GetBlobClient(name).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = ZipContentType } },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.GetBlobClient(name).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // the container does not exist yet
        }
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var deleted = 0;

        try
        {
            await foreach (var blob in _container.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (blob.Properties.LastModified is { } modified && modified < threshold)
                {
                    await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
                    deleted++;
                }
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // nothing was ever stored
        }

        return deleted;
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_isContainerReady)
        {
            return;
        }

        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        _isContainerReady = true;
    }
}
