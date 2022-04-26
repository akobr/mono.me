using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class DocumentInfo
    {
        public sealed class Builder : BaseBuilder
        {
            internal Builder(DocumentInfo document)
                : base(document)
            {
                Checksum = document.Checksum;
                Types = document.types.ToBuilder();
            }

            public string Checksum { get; set; }

            public ImmutableHashSet<IIdentificator>.Builder Types { get; private set; }

            public void AddTypeKey(IIdentificator typeKey)
            {
                Types.Add(typeKey);
            }

            public DocumentInfo ToImmutable()
            {
                return new DocumentInfo(this);
            }
        }
    }
}
