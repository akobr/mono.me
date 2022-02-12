namespace _42.Monorepo.Texo.Core.Markdown
{
    public interface IMarkdownService
    {
        Markdig.MarkdownPipeline Pipeline { get; }

        string Normalise(string text);

        string ToHtml(string text);

        string ToPlainText(string text);

        Markdig.Syntax.MarkdownDocument Parse(string text);
    }
}
