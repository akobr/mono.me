using System;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public class ExactVersions : IExactVersions
    {
        public ExactVersions()
        {
            Version = PackageVersion = new SemVersion(0);
            AssemblyVersion = AssemblyFileVersion = new Version(0, 0);
            AssemblyInformationalVersion = "0.0.0";
        }

        public ExactVersions(Version version)
        {
            Version = PackageVersion = new SemVersion(version);
            AssemblyVersion = AssemblyFileVersion = version;
            AssemblyInformationalVersion = version.ToString();
        }

        public SemVersion Version { get; init; }

        public Version AssemblyVersion { get; init; }

        public Version AssemblyFileVersion { get; init; }

        public string AssemblyInformationalVersion { get; init; }

        public SemVersion PackageVersion { get; init; }
    }
}
