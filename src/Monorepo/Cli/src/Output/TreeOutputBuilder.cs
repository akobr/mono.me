using System;
using System.Collections.Generic;
using System.Text;

namespace _42.Monorepo.Cli.Output
{
    public class TreeOutputBuilder : ITreeOutputBuilder
    {
        public string BuildTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction, int leftIndentation = 0)
        {
            var @static = new StaticContext<T>(
                new StringBuilder(),
                new List<char> { '\0' },
                nodeRenderFunction)
            {
                LeftIndentation = leftIndentation,
            };

            RenderNode(new NodeRenderingContext<T>(@static, root));

            return @static.Result.ToString();
        }

        private void RenderNode<T>(NodeRenderingContext<T> context)
        {
            RenderIndention(context);
            RenderNodeContent(context);
            RenderChildren(context);
        }

        private void RenderChildren<T>(NodeRenderingContext<T> context)
        {
            var node = context.Node;
            var count = node.Children.Count;
            var index = 0;

            UpdateIndentionBeforeChildren(context);

            foreach (var child in node.Children)
            {
                RenderNode(new NodeRenderingContext<T>(context.Static, child)
                {
                    Level = context.Level + 1,
                    IsLast = ++index == count,
                });
            }

            UpdateIndentionAfterChildren(context);
        }

        private static void RenderNodeContent<T>(NodeRenderingContext<T> context)
        {
            context.Static.Result.AppendLine(context.Static.RenderFunction(context.Node.Content));
        }

        private static void RenderIndention<T>(NodeRenderingContext<T> context)
        {
            var intentMask = context.Static.IndentMask;
            var result = context.Static.Result;

            if (context.IsLast)
            {
                intentMask[context.Level] = '└';
            }

            if (context.Static.LeftIndentation > 0)
            {
                result.Append(' ', context.Static.LeftIndentation);
            }

            for (var i = 1; i < intentMask.Count; i++)
            {
                result.Append(' ');
                result.Append(intentMask[i]);
            }

            result.Append(' ');
        }

        private static void UpdateIndentionBeforeChildren<T>(NodeRenderingContext<T> context)
        {
            var intentMask = context.Static.IndentMask;
            intentMask[context.Level] = context.IsLast ? ' ' : '│';
            intentMask.Add('├');
        }

        private static void UpdateIndentionAfterChildren<T>(NodeRenderingContext<T> context)
        {
            var intentMask = context.Static.IndentMask;
            intentMask.RemoveAt(context.Level + 1);

            if (!context.IsLast)
            {
                intentMask[context.Level] = '├';
            }
        }

        private class NodeRenderingContext<T>
        {
            public NodeRenderingContext(StaticContext<T> @static, IComposition<T> node)
            {
                Static = @static;
                Node = node;
            }

            public StaticContext<T> Static { get; }

            public IComposition<T> Node { get; }

            public int Level { get; init; }

            public bool IsLast { get; init; }
        }

        private class StaticContext<T>
        {
            public StaticContext(StringBuilder result, List<char> indentMask, Func<T, string> renderFunction)
            {
                Result = result;
                IndentMask = indentMask;
                RenderFunction = renderFunction;
            }

            public StringBuilder Result { get; }

            public List<char> IndentMask { get; }

            public Func<T, string> RenderFunction { get; }

            public int LeftIndentation { get; init; }
        }
    }
}
