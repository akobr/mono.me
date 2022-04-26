using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class NamespaceInfo
    {
        public sealed class Builder : BaseBuilder
        {
            internal Builder(NamespaceInfo namespaceInfo)
                : base(namespaceInfo)
            {
                Types = namespaceInfo.types.ToBuilder();
            }

            public ImmutableHashSet<IIdentificator>.Builder Types { get; private set; }

            public void AddTypeKey(IIdentificator typeKey)
            {
                Types.Add(typeKey);
            }

            public NamespaceInfo ToImmutable()
            {
                return new NamespaceInfo(this);
            }
        }
    }
}
