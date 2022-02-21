using Semver;

namespace _42.Monorepo.Cli.Model
{
    public class ExternalDependency : IExternalDependency
    {
        public ExternalDependency(string name, SemVersion version, bool isDirect)
        {
            Name = name;
            Version = version;
            IsDirect = isDirect;
        }

        public string Name { get; }

        public SemVersion Version { get; }

        public bool IsDirect { get; }
    }
}
