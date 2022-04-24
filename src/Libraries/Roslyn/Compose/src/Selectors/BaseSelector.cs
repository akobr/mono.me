using System;
using System.Collections.Generic;
using System.IO;
using _42.Roslyn.Compose.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace _42.Roslyn.Compose.Selectors
{
    public class BaseSelector<T>
        where T : IComposer
    {
        private readonly Stack<SelectedObject> _nodes = new Stack<SelectedObject>();

        public SyntaxNode CurrentNode => _nodes.Peek()?.CurrentNode;

        public List<SyntaxNode> CurrentNodesList => _nodes.Peek()?.CurrentNodesList;

        protected BaseSelector()
        {
        }

        public BaseSelector(StreamReader reader)
        {
            var code = reader.ReadToEnd();
            _nodes.Push(new SelectedObject(SyntaxFactory.ParseCompilationUnit(code)));
        }

        public BaseSelector(SyntaxNode node)
        {
            _nodes.Push(new SelectedObject(node));
        }

        public BaseSelector(List<SyntaxNode> nodes)
        {
            _nodes.Push(new SelectedObject(nodes));
        }

        protected T? Composer { get; set; }

        public bool IsAtRoot()
        {
            return _nodes.Count == 1;
        }

        protected void NextStep(SyntaxNode node)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"{typeof(T).Name}: Selection failed!");
            }

            _nodes.Push(new SelectedObject(node));
        }

        public T Reset()
        {
            while (_nodes.Count > 1)
            {
                _nodes.Pop();
            }

            return Composer;
        }

        public T StepBack()
        {
            if(_nodes.Peek() != null && _nodes.Count > 1)
            {
                _nodes.Pop();
            }

            return Composer;
        }

        protected void NextStep(List<SyntaxNode> nodes)
        {
            if (nodes == null)
            {
                throw new InvalidOperationException($"{typeof(T).Name}: Selection failed!");
            }

            this._nodes.Push(new SelectedObject(nodes));
        }

        protected void SetHead(SyntaxNode node)
        {
            _nodes.Clear();
            _nodes.Push(new SelectedObject(node));
        }

        protected void ReplaceHead(SyntaxNode node)
        {
            _nodes.Pop();
            _nodes.Push(new SelectedObject(node));
        }
    }
}
