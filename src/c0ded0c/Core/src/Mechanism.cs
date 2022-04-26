using c0ded0c.Core.Genesis;
using c0ded0c.Core.Mining;

namespace c0ded0c.Core
{
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
    public class Mechanism : IMechanism
    {
        public IMiningEngine Mining { get; set; }

        public IProjectProcessingEngine ProjectPreceeding { get; set; }

        public IAssemblyProcessingEngine AssemblyPreceeding { get; set; }

        public IStoringEngine Storing { get; set; }

        public IGenesisEngine Genesis { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
}
