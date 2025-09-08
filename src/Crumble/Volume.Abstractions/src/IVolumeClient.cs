namespace _42.Crumble;

public interface IVolumeClient
{
    Task<Stream> GetFileContentAsync(string filePath, CancellationToken cancellationToken = default);

    Task WriteFileContentAsync(string filePath, Stream content, CancellationToken cancellationToken = default);
}
