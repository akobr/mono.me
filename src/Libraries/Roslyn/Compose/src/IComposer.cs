using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace _42.Roslyn.Compose
{
    public interface IComposer
    {
        IComposer? ParentComposer { get; set; }

        List<SyntaxNode> Replace(SyntaxNode oldNode, SyntaxNode newNode, List<SyntaxNode> trackedNodes);
    }
}
