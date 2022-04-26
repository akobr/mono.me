using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace c0ded0c.Core
{
    public sealed partial class DocumentInfo : SubjectInfo, IDocumentInfo
    {
        private ImmutableHashSet<IIdentificator> types;

        public DocumentInfo(IIdentificator key)
            : base(key)
        {
            types = ImmutableHashSet<IIdentificator>.Empty;
        }

        private DocumentInfo(DocumentInfo toClone)
            : base(toClone)
        {
            Checksum = toClone.Checksum;
            types = toClone.types;
        }

        private DocumentInfo(Builder builder)
            : base(builder)
        {
            Checksum = builder.Checksum;
            types = builder.Types.ToImmutable();
        }

        [InfoProperty]
        public string Checksum { get; }

        [Artifact]
        public IImmutableSet<IIdentificator> Types => types;

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return Enumerable.Empty<ISubjectInfo>();
        }

        public DocumentInfo AddType(IIdentificator identificator)
        {
            return new DocumentInfo(this)
            {
                types = types.Add(identificator),
            };
        }

        public DocumentInfo RemoveType(IIdentificator identificator)
        {
            return new DocumentInfo(this)
            {
                types = types.Remove(identificator),
            };
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }
    }
}
