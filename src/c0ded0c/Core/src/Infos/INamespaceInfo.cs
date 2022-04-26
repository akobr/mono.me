using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface INamespaceInfo : ISubjectInfo
    {
        [Artifact]
        IImmutableSet<IIdentificator> Types { get; }
    }
}
