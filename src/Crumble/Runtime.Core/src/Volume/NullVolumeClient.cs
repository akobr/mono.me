namespace _42.Crumble;

public class NullVolumeClient : IVolumeClient
{
    public Task<Stream> GetContent(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Stream.Null);
    }

    public Task WriteContent(string path, Stream content, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<TModel?> GetModel<TModel>(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(default(TModel));
    }

    public Task WriteModel<TModel>(string path, TModel model, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<string> GetItems(string path, CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<string>();
    }
}

public class NullVolumeClient<TVolumeContext> : NullVolumeClient, IVolumeClient<TVolumeContext>
    where TVolumeContext : IVolumeContext;
