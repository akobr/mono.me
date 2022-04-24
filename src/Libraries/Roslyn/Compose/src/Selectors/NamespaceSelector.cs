using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace _42.Roslyn.Compose.Selectors
{
    public class NamespaceSelector<T> : BaseSelector<T> where T:IComposer
    {
        protected NamespaceSelector()
        {
        }

        public NamespaceSelector(StreamReader reader) : base(reader)
        {

        }

        public NamespaceSelector(SyntaxNode node) : base(node)
        {

        }

        public ClassComposer ToClassComposer()
        {
            if (CurrentNode is ClassDeclarationSyntax)
            {
                return new ClassComposer(CurrentNode as ClassDeclarationSyntax, Composer);
            }

            return null;
        }


        public T SelectClassDeclaration(string className)
        {
            var @class = CurrentNode?.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(cl => cl.Identifier.ToString() == className).FirstOrDefault();
            
            NextStep(@class);
            return Composer;
        }

        public T SelectInterfaceDeclaration(string interfaceName)
        {
            var @interface = CurrentNode?.DescendantNodes().OfType<InterfaceDeclarationSyntax>()
                .Where(cl => cl.Identifier.ToString() == interfaceName).FirstOrDefault();

            NextStep(@interface);
            return Composer;
        }

        public T SelectEnumDeclaration(string enumName)
        {
            var @enum = CurrentNode?.DescendantNodes().OfType<EnumDeclarationSyntax>()
                .Where(en => en.Identifier.ToString() == enumName).FirstOrDefault();

            NextStep(@enum);
            return Composer;
        }

        public T SelectStructDeclaration(string structName)
        {
            var @struct = CurrentNode?.DescendantNodes().OfType<StructDeclarationSyntax>()
                .Where(st => st.Identifier.ToString() == structName).FirstOrDefault();

            NextStep(@struct);
            return Composer;
        }
    }
}
