using System;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public class ExactVersions : IExactVersions
    {
        public ExactVersions()
        {
            Version = AssemblyVersion = new Version(0, 0);
            AssemblyInformationalVersion = "0.0.0";
            PackageVersion = new SemVersion(0);
        }

        public ExactVersions(Version version)
        {
            Version = AssemblyVersion = version;
            AssemblyInformationalVersion = version.ToString();
            PackageVersion = new SemVersion(version);
        }

        public Version Version { get; init; }

        public Version AssemblyVersion { get; init; }

        public string AssemblyInformationalVersion { get; init; }

        public SemVersion PackageVersion { get; init; }
    }
}
