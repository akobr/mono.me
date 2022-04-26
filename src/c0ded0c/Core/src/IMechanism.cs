using c0ded0c.Core.Genesis;
using c0ded0c.Core.Mining;

namespace c0ded0c.Core
{
    public interface IMechanism
    {
        IMiningEngine Mining { get; }

        IProjectProcessingEngine ProjectPreceeding { get; }

        IAssemblyProcessingEngine AssemblyPreceeding { get; }

        IStoringEngine Storing { get; }

        IGenesisEngine Genesis { get; }
    }
}
