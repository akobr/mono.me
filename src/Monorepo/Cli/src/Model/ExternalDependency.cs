using Semver;

namespace _42.Monorepo.Cli.Model
{
    public class ExternalDependency : IExternalDependency
    {
        public ExternalDependency(string name, SemVersion version)
        {
            Name = name;
            Version = version;
        }

        public string Name { get; }

        public SemVersion Version { get; }

        public bool IsDirect { get; set; }
    }
}
