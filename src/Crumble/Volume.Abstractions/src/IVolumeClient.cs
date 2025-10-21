namespace _42.Crumble;

public interface IVolumeClient
{
    Task<Stream> GetContent(string path, CancellationToken cancellationToken = default);

    Task WriteContent(string path, Stream content, CancellationToken cancellationToken = default);

    Task<TModel> GetModel<TModel>(string path, CancellationToken cancellationToken = default);

    Task WriteModel<TModel>(string path, TModel model, CancellationToken cancellationToken = default);
}
