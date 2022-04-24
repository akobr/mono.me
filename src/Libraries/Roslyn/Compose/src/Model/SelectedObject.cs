using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace _42.Roslyn.Compose.Model
{
    public class SelectedObject
    {
        public SelectedObject(SyntaxNode node)
        {
            CurrentNode = node;
        }

        public SelectedObject(List<SyntaxNode> nodeList)
        {
            CurrentNodesList = nodeList;
        }

        public SyntaxNode CurrentNode { get; }

        public List<SyntaxNode> CurrentNodesList { get; }
    }
}
