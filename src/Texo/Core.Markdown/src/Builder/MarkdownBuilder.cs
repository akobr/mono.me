using System;
using System.Collections.Generic;
using System.Text;

namespace _42.Monorepo.Texo.Core.Markdown.Builder
{
    public class MarkdownBuilder : IMarkdownBuilder
    {
        private const char NEWLINE = '\n';
        private readonly StringBuilder stringBuilder;

        public MarkdownBuilder()
        {
            stringBuilder = new StringBuilder();
        }

        private char LastCharacter => stringBuilder.Length < 1
            ? '\0'
            : stringBuilder[^1];

        public IMarkdownBuilder Header(string text)
        {
            return Header(text, 1);
        }

        public IMarkdownBuilder Header(string text, int level)
        {
            if (!IsOnNewLine())
            {
                stringBuilder.AppendLine();
            }

            stringBuilder.Append('#', Math.Max(level, 1));
            stringBuilder.Append(' ');
            stringBuilder.AppendLine(text);
            return this;
        }

        public IMarkdownBuilder Bullet(string text)
        {
            if (!IsOnNewLine())
            {
                stringBuilder.AppendLine();
            }

            stringBuilder.Append("- ");
            stringBuilder.AppendLine(text);
            return this;
        }

        public IMarkdownBuilder Bullet(string text, int intentLevel)
        {
            Bullet(intentLevel);
            stringBuilder.AppendLine(text);
            return this;
        }

        public IMarkdownBuilder Bullet(int intentLevel = 0)
        {
            if (!IsOnNewLine())
            {
                stringBuilder.AppendLine();
            }

            stringBuilder.Append(' ', Math.Max(intentLevel, 0) * 2);
            stringBuilder.Append("- ");
            return this;
        }

        public IMarkdownBuilder Image(string path)
        {
            stringBuilder.AppendFormat("![][{0}]", path);
            return this;
        }

        public IMarkdownBuilder Image(string path, string alternative)
        {
            stringBuilder.AppendFormat("![{0}]({1})", alternative, path);
            return this;
        }

        public IMarkdownBuilder Link(string title, string path)
        {
            stringBuilder.AppendFormat("[{0}]({1})", title, path);
            return this;
        }

        public IMarkdownBuilder Link(ILink link)
        {
            stringBuilder.AppendFormat("[{0}]({1})", link.Title, link.Address.AbsoluteUri);
            return this;
        }

        public IMarkdownBuilder Italic(string text)
        {
            stringBuilder.AppendFormat("*{0}*", text);
            return this;
        }

        public IMarkdownBuilder Bold(string text)
        {
            stringBuilder.AppendFormat("**{0}**", text);
            return this;
        }

        public IMarkdownBuilder Marked(string text)
        {
            stringBuilder.AppendFormat("=={0}==", text);
            return this;
        }

        public IMarkdownBuilder CodeBlock(string language, string code)
        {
            if (!IsOnNewLine())
            {
                stringBuilder.AppendLine();
            }

            stringBuilder.AppendLine($"```{language}");
            stringBuilder.AppendLine(code);
            stringBuilder.AppendLine("```");
            return this;
        }

        public IMarkdownBuilder CodeInline(string code)
        {
            stringBuilder.AppendFormat("`{0}`", code);
            return this;
        }

        public IMarkdownBuilder Blockquotes(string quotes)
        {
            if (!IsOnNewLine())
            {
                stringBuilder.AppendLine();
            }

            foreach (var line in GetLines(quotes))
            {
                stringBuilder.AppendFormat("> {0}", line);
                stringBuilder.AppendLine();
            }

            return this;
        }

        public IMarkdownBuilder ContainerBlock(string className, string text)
        {
            if (!IsOnNewLine())
            {
                stringBuilder.AppendLine();
            }

            stringBuilder.AppendLine($":::{className}");
            stringBuilder.AppendLine(text);
            stringBuilder.AppendLine(":::");
            return this;
        }

        public IMarkdownBuilder ContainerInline(string className, string text)
        {
            stringBuilder.AppendFormat("::{0}::{{.{1}}}", text, className);
            return this;
        }

        public IMarkdownBuilder WriteLine()
        {
            stringBuilder.AppendLine();
            return this;
        }

        public IMarkdownBuilder WriteLine(string text)
        {
            stringBuilder.AppendLine(text);
            return this;
        }

        public IMarkdownBuilder Write(string text)
        {
            stringBuilder.Append(text);
            return this;
        }

        public override string ToString()
        {
            return stringBuilder.ToString();
        }

        private bool IsOnNewLine()
        {
            return stringBuilder.Length < 1 || LastCharacter == NEWLINE;
        }

        private static IEnumerable<string> GetLines(string text)
        {
            return text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        }
    }
}
