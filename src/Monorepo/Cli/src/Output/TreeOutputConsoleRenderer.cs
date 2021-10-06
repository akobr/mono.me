using System;
using System.Collections.Generic;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Output
{
    public class TreeOutputConsoleRenderer : ITreeOutputRenderer
    {
        private readonly IExtendedConsole console;

        public TreeOutputConsoleRenderer(IExtendedConsole console)
        {
            this.console = console;
        }

        public void RenderTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction, int leftIndentation = 0)
        {
            var @static = new StaticContext<T>(
                console,
                new List<char> { '\0' },
                nodeRenderFunction)
            {
                LeftIndentation = leftIndentation,
            };

            RenderNode(new NodeRenderingContext<T>(@static, root));
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
            context.Static
                .RenderFunction(context.Node.Content)
                .WriteTo(context.Static.Console);

            context.Static.Console.Console.WriteLine();
        }

        private static void RenderIndention<T>(NodeRenderingContext<T> context)
        {
            var intentMask = context.Static.IndentMask;
            var console = context.Static.Console.Console;

            if (context.IsLast)
            {
                intentMask[context.Level] = '└';
            }

            if (context.Static.LeftIndentation > 0)
            {
                console.Write(new string(' ', context.Static.LeftIndentation));
            }

            for (var i = 1; i < intentMask.Count; i++)
            {
                console.Write(' ');
                console.Write(intentMask[i]);
            }

            console.Write(' ');
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
            public StaticContext(IExtendedConsole console, List<char> indentMask, Func<T, IConsoleOutput> renderFunction)
            {
                Console = console;
                IndentMask = indentMask;
                RenderFunction = renderFunction;
            }

            public IExtendedConsole Console { get; }

            public List<char> IndentMask { get; }

            public Func<T, IConsoleOutput> RenderFunction { get; }

            public int LeftIndentation { get; init; }
        }
    }
}
