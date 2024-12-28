namespace _42.tHolistic;

public interface IResultAttachmentsService
{
    Task<string> CreateAttachmentAsync(Stream content, string relativePath);

    Task<string> CreateJsonAttachmentAsync<TModel>(TModel content, string relativePath);
}
