using System;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public class RoslynDocumentInfo
    {
        public RoslynDocumentInfo(Document document, SyntaxNode syntaxRoot, SemanticModel semanticModel)
        {
            Document = document;
            RootSyntaxNode = syntaxRoot;
            SemanticModel = semanticModel;
        }

        public Document Document { get; }

        public SyntaxNode RootSyntaxNode { get; }

        public SemanticModel SemanticModel { get; }
    }
}
