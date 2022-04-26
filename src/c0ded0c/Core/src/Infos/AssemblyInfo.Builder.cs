using System.Collections.Immutable;
using Semver;

namespace c0ded0c.Core
{
    public partial class AssemblyInfo
    {
        public sealed class Builder : BaseBuilder
        {
            internal Builder(AssemblyInfo assembly)
                : base(assembly)
            {
                Version = assembly.Version;
                Namespaces = assembly.namespaces.ToBuilder();
                Types = assembly.types.ToBuilder();
                Members = assembly.members.ToBuilder();
                Documents = assembly.documents.ToBuilder();
            }

            public SemVersion Version { get; set; }

            public ImmutableDictionary<string, INamespaceInfo>.Builder Namespaces { get; private set; }

            public ImmutableDictionary<string, ITypeInfo>.Builder Types { get; private set; }

            public ImmutableDictionary<string, IMemberInfo>.Builder Members { get; private set; }

            public ImmutableDictionary<string, IDocumentInfo>.Builder Documents { get; private set; }

            public AssemblyInfo ToImmutable()
            {
                return new AssemblyInfo(this);
            }
        }
    }
}
