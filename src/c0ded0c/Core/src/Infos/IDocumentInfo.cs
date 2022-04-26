using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface IDocumentInfo : ISubjectInfo
    {
        [InfoProperty]
        string Checksum { get; }

        [Artifact]
        IImmutableSet<IIdentificator> Types { get; }
    }
}
