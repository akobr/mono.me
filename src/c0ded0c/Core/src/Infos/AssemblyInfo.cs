using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Semver;

namespace c0ded0c.Core
{
    public sealed partial class AssemblyInfo : SubjectInfo, IAssemblyInfo
    {
        private ImmutableDictionary<string, INamespaceInfo> namespaces;
        private ImmutableDictionary<string, ITypeInfo> types;
        private ImmutableDictionary<string, IMemberInfo> members;
        private ImmutableDictionary<string, IDocumentInfo> documents;

        public AssemblyInfo(IIdentificator key)
            : base(key)
        {
            Version = new SemVersion(0);
            namespaces = ImmutableDictionary<string, INamespaceInfo>.Empty.WithComparers(StringComparer.Ordinal);
            types = ImmutableDictionary<string, ITypeInfo>.Empty.WithComparers(StringComparer.Ordinal);
            members = ImmutableDictionary<string, IMemberInfo>.Empty.WithComparers(StringComparer.Ordinal);
            documents = ImmutableDictionary<string, IDocumentInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        }

        public AssemblyInfo(IAssemblyInfo toClone)
            : base(toClone)
        {
            Version = toClone.Version;
            namespaces = ImmutableDictionary.CreateRange(StringComparer.Ordinal, toClone.Namespaces);
            types = ImmutableDictionary.CreateRange(StringComparer.Ordinal, toClone.Types);
            members = ImmutableDictionary<string, IMemberInfo>.Empty.WithComparers(StringComparer.Ordinal);
            documents = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, toClone.Documents);
        }

        private AssemblyInfo(AssemblyInfo toClone)
            : base(toClone)
        {
            Version = toClone.Version;
            namespaces = toClone.namespaces;
            types = toClone.types;
            members = toClone.members;
            documents = toClone.documents;
        }

        private AssemblyInfo(Builder builder)
            : base(builder)
        {
            Version = builder.Version;
            namespaces = builder.Namespaces.ToImmutable();
            types = builder.Types.ToImmutable();
            members = builder.Members.ToImmutable();
            documents = builder.Documents.ToImmutable();
        }

        [InfoProperty]
        public SemVersion Version { get; private set; }

        [Artifact]
        public IImmutableDictionary<string, INamespaceInfo> Namespaces => namespaces;

        [Artifact]
        public IImmutableDictionary<string, ITypeInfo> Types => types;

        [Artifact]
        public IImmutableDictionary<string, IMemberInfo> Members => members;

        [Artifact]
        public IImmutableDictionary<string, IDocumentInfo> Documents => documents;

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return types.Values;
        }

        public AssemblyInfo SetNamespace(INamespaceInfo namespaceInfo)
        {
            return new AssemblyInfo(this)
            {
                namespaces = namespaces.Remove(namespaceInfo.Name).SetItem(namespaceInfo.Name, namespaceInfo),
            };
        }

        public AssemblyInfo RemoveNamespace(string name)
        {
            return new AssemblyInfo(this)
            {
                namespaces = namespaces.Remove(name),
            };
        }

        public AssemblyInfo SetType(ITypeInfo typeInfo)
        {
            return new AssemblyInfo(this)
            {
                types = types.Remove(typeInfo.Key.FullName).SetItem(typeInfo.Key.FullName, typeInfo),
            };
        }

        public AssemblyInfo RemoveType(string fullName)
        {
            return new AssemblyInfo(this)
            {
                types = types.Remove(fullName),
            };
        }

        public AssemblyInfo SetDocument(IDocumentInfo documentInfo)
        {
            return new AssemblyInfo(this)
            {
                documents = documents.Remove(documentInfo.Key.FullName).SetItem(documentInfo.Key.FullName, documentInfo),
            };
        }

        public AssemblyInfo RemoveDocument(string relativePath)
        {
            return new AssemblyInfo(this)
            {
                documents = documents.Remove(relativePath),
            };
        }

        public AssemblyInfo SetExpansion(IExpansion expansion)
        {
            return SetExpansion(new AssemblyInfo(this), expansion);
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }

        public static AssemblyInfo From(IAssemblyInfo assembly)
        {
            return assembly is AssemblyInfo familiar
                ? familiar
                : new AssemblyInfo(assembly);
        }
    }
}
