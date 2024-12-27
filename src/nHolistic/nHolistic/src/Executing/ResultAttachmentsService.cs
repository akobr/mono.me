using System.Text.Json;

namespace _42.nHolistic;

public class ResultAttachmentsService(IRunDirectoryProvider runDirectory) : IResultAttachmentsService
{
    private readonly string rootFullPath = Path.GetFullPath(Path.Combine(runDirectory.GetRunDirectory(), "attachments"));

    public async Task<string> CreateAttachmentAsync(Stream content, string relativePath)
    {
        var path = PrepareFilePath(relativePath);
        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream);
        return path;
    }

    public async Task<string> CreateJsonAttachmentAsync<TModel>(TModel content, string relativePath)
    {
        var path = PrepareFilePath(relativePath);
        await using var fileStream = File.Create(path);
        await JsonSerializer.SerializeAsync(fileStream, content);
        return path;
    }

    private string PrepareFilePath(string relativePath)
    {
        var path = Path.Combine(rootFullPath, relativePath);
        var directory = Path.GetDirectoryName(path);

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }
}
