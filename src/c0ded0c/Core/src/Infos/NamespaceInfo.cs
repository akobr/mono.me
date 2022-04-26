using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace c0ded0c.Core
{
    public sealed partial class NamespaceInfo : SubjectInfo, INamespaceInfo
    {
        private ImmutableHashSet<IIdentificator> types;

        public NamespaceInfo(IIdentificator key)
            : base(key)
        {
            types = ImmutableHashSet<IIdentificator>.Empty;
        }

        public NamespaceInfo(INamespaceInfo toClone)
            : base(toClone)
        {
            types = ImmutableHashSet<IIdentificator>.Empty.Union(toClone.Types);
        }

        private NamespaceInfo(NamespaceInfo toClone)
            : base(toClone)
        {
            types = toClone.types;
        }

        private NamespaceInfo(Builder builder)
            : base(builder)
        {
            types = builder.Types.ToImmutable();
        }

        [Artifact]
        public IImmutableSet<IIdentificator> Types => types;

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return Enumerable.Empty<ISubjectInfo>();
        }

        public NamespaceInfo AddType(IIdentificator identificator)
        {
            return new NamespaceInfo(this)
            {
                types = types.Add(identificator),
            };
        }

        public NamespaceInfo RemoveType(IIdentificator identificator)
        {
            return new NamespaceInfo(this)
            {
                types = types.Remove(identificator),
            };
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }

        public static NamespaceInfo From(INamespaceInfo @namespace)
        {
            return @namespace is NamespaceInfo familiar
                ? familiar
                : new NamespaceInfo(@namespace);
        }
    }
}
