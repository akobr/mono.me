using c0ded0c.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using DocumentInfo = c0ded0c.Core.DocumentInfo;
using TypeInfo = c0ded0c.Core.TypeInfo;

namespace c0ded0c.MsBuild
{
    public class TypesAndMembersExtractorWalker : CSharpSyntaxWalker
    {
        private readonly RoslynDocumentInfo roslynDocument;
        private readonly DocumentInfo.Builder document;
        private readonly AssemblyInfo.Builder assembly;
        private readonly IPathCalculatorProvider pathCalculatorProvider;
        private readonly IIdentificationBuilder keyBuilder;
        private readonly INamespaceManager namespaceManager;

        private NamespaceInfo.Builder? namespaceBuilder;

        public TypesAndMembersExtractorWalker(
            RoslynDocumentInfo roslynDocument,
            DocumentInfo.Builder document,
            AssemblyInfo.Builder assembly,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder,
            INamespaceManager namespaceManager)
        {
            this.roslynDocument = roslynDocument;
            this.document = document;
            this.assembly = assembly;
            this.pathCalculatorProvider = pathCalculatorProvider;
            this.keyBuilder = keyBuilder;
            this.namespaceManager = namespaceManager;
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            namespaceBuilder = GetOrBuildNamespaceInfo(node);

            if (namespaceBuilder == null)
            {
                return;
            }

            base.VisitNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            BuildAndSaveTypeInfo(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            BuildAndSaveTypeInfo(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            BuildAndSaveTypeInfo(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            BuildAndSaveTypeInfo(node);
        }

        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            INamedTypeSymbol? typeSymbol = roslynDocument.SemanticModel.GetDeclaredSymbol(node);
            TypeInfo? type = BuildTypeInfo(typeSymbol);
            SaveTypeInfo(type);
        }

        private void BuildAndSaveTypeInfo(BaseTypeDeclarationSyntax node)
        {
            INamedTypeSymbol? typeSymbol = roslynDocument.SemanticModel.GetDeclaredSymbol(node);
            TypeInfo? type = BuildTypeInfo(typeSymbol);
            SaveTypeInfo(type);
        }

        private TypeInfo? BuildTypeInfo(INamedTypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return null;
            }

            TypeInfo.Builder typeBuilder = CreateTypeInfo(typeSymbol);
            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                // skips backing fields and generated stuff
                if (member.IsImplicitlyDeclared)
                {
                    continue;
                }

                if (member is INamedTypeSymbol nestedType)
                {
                    TypeInfo? nestedTypeInfo = BuildTypeInfo(nestedType);

                    if (nestedTypeInfo != null)
                    {
                        typeBuilder.NestedTypes.Add(nestedTypeInfo.Key.Name, nestedTypeInfo);
                        SaveTypeInfo(nestedTypeInfo);
                    }

                    continue;
                }

                MemberInfo memberInfo = member.BuildInfo(typeBuilder.Key, document.Key, assembly.Key, pathCalculatorProvider, keyBuilder);
                typeBuilder.Members.Add(memberInfo.Key.Name, memberInfo);
                assembly.Members.Add(memberInfo.Key.FullName, memberInfo);
            }

            return typeBuilder.ToImmutable();
        }

        private TypeInfo.Builder CreateTypeInfo(INamedTypeSymbol typeSymbol)
        {
            TypeInfo.Builder typeBuilder = typeSymbol.BuildInfo(document.Key, assembly.Key, pathCalculatorProvider, keyBuilder).ToBuilder();
            typeBuilder.MutableTag = typeSymbol;
            return typeBuilder;
        }

        private void SaveTypeInfo(TypeInfo? type)
        {
            if (type == null)
            {
                return;
            }

            namespaceBuilder?.Types.Add(type.Key);
            document.Types.Add(type.Key);
            assembly.Types.Add(type.Key.FullName, type);
        }

        private NamespaceInfo.Builder? GetOrBuildNamespaceInfo(NamespaceDeclarationSyntax node)
        {
            INamespaceSymbol? namespaceSymbol = roslynDocument.SemanticModel.GetDeclaredSymbol(node);

            if (namespaceSymbol == null)
            {
                return null;
            }

            return namespaceManager.GetOrSetNamespace(namespaceSymbol, assembly.Key);
        }
    }
}
