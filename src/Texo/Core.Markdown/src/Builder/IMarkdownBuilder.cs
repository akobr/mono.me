namespace _42.Monorepo.Texo.Core.Markdown.Builder
{
    public interface IMarkdownBuilder
    {
        IMarkdownBuilder Header(string text);

        IMarkdownBuilder Header(string text, int level);

        IMarkdownBuilder Bullet(string text);

        IMarkdownBuilder Bullet(string text, int intentLevel);

        IMarkdownBuilder Image(string path);

        IMarkdownBuilder Image(string path, string alternative);

        IMarkdownBuilder Link(string title, string path);

        IMarkdownBuilder Link(ILink link);

        IMarkdownBuilder Italic(string text);

        IMarkdownBuilder Bold(string text);

        IMarkdownBuilder Marked(string text);

        IMarkdownBuilder CodeBlock(string language, string code);

        IMarkdownBuilder CodeInline(string code);

        IMarkdownBuilder Blockquotes(string quotes);

        IMarkdownBuilder WriteLine();

        IMarkdownBuilder WriteLine(string text);

        IMarkdownBuilder Write(string text);
    }
}
