using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace _42.Crumble;

public class BlobVolumeClient<TContext>(BlobContainerClient client, IVolumeContext context) : IVolumeClient<TContext>
    where TContext : IVolumeContext
{
    public async Task<Stream> GetContent(string path, CancellationToken cancellationToken = default)
    {
        var blobClient = client.GetBlobClient(GetBlobName(path));
        var existsResponse = await blobClient.ExistsAsync(cancellationToken);

        if (!existsResponse.Value)
        {
            return Stream.Null;
        }

        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public Task WriteContent(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var blobClient = client.GetBlobClient(GetBlobName(path));
        return blobClient.UploadAsync(content, true, cancellationToken: cancellationToken);
    }

    public Task<TModel> GetModel<TModel>(string path, CancellationToken cancellationToken = default)
    {
        // TODO: Use System.Text.Json
        throw new System.NotImplementedException();
    }

    public Task WriteModel<TModel>(string path, TModel model, CancellationToken cancellationToken = default)
    {
        // TODO: Use System.Text.Json
        throw new System.NotImplementedException();
    }

    public async IAsyncEnumerable<string> GetItems(string path, CancellationToken cancellationToken = default)
    {
        var pathPrefix = GetBlobName(path);
        var blobs = client.GetBlobsAsync(BlobTraits.None, BlobStates.None, pathPrefix, cancellationToken: cancellationToken);
        await foreach (var blob in blobs)
        {
            yield return blob.Name;
        }
    }

    private string GetBlobName(string path)
    {
        if (path.StartsWith(Path.DirectorySeparatorChar) || path.StartsWith(Path.AltDirectorySeparatorChar))
        {
            return $"{context.RootPath}/{path[1..]}";
        }

        return $"{context.RootPath}/{context.ItemPath}/{path}";
    }
}
