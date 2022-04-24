using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace _42.Roslyn.Compose.Selectors
{
    public class CompilationUnitSelector<T> : BaseSelector<T>
        where T : IComposer
    {
        public CompilationUnitSelector(StreamReader sr)
            : base(sr)
        {
        }

        protected CompilationUnitSelector()
        {
        }

        public NamespaceComposer ToNamespaceComposer()
        {
            if (CurrentNode != null && CurrentNode is NamespaceDeclarationSyntax)
            {
                return new NamespaceComposer(CurrentNode as NamespaceDeclarationSyntax, Composer);
            }

            return null;
        }

        public T SelectNamespace()
        {
            var @namespace = CurrentNode?
                .DescendantNodes()
                .OfType<NamespaceDeclarationSyntax>()
                .FirstOrDefault();

            if (@namespace != null)
            {
                NextStep(@namespace);
            }

            return Composer;
        }
    }
}
