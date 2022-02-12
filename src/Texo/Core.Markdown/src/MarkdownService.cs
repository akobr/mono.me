using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;

namespace _42.Monorepo.Texo.Core.Markdown
{
    public class MarkdownService : IMarkdownService
    {
        private readonly MarkdownPipeline pipeline;

        public MarkdownService()
        {
            pipeline = new MarkdownPipelineBuilder()
                .UseAutoLinks()
                .UseEmphasisExtras(EmphasisExtraOptions.Default)
                .UseTaskLists()
                .UseCustomContainers()
                .UseFigures()
                .UseFooters()
                .UseCitations()
                .Build();
        }

        public MarkdownPipeline Pipeline => pipeline;

        public string Normalise(string text)
        {
            return Markdig.Markdown.Normalize(text, null, pipeline);
        }

        public string ToHtml(string text)
        {
            return Markdig.Markdown.ToHtml(text, pipeline);
        }

        public string ToPlainText(string text)
        {
            return Markdig.Markdown.ToPlainText(text, pipeline);
        }

        public MarkdownDocument Parse(string text)
        {
            return Markdig.Markdown.Parse(text, pipeline);
        }
    }
}
