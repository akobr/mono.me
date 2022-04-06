using System;
using _42.Monorepo.Cli.Extensions;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public class ExactVersions : IExactVersions
    {
        public ExactVersions()
        {
            Version = new Version(0, 0);
            SemVersion = PackageVersion = new SemVersion(0);
            AssemblyVersion = AssemblyFileVersion = new Version(0, 0);
            AssemblyInformationalVersion = "0.0.0";
        }

        public ExactVersions(Version version)
        {
            Version = version;
            SemVersion = PackageVersion = version.ToSemVersion();
            AssemblyVersion = AssemblyFileVersion = version;
            AssemblyInformationalVersion = version.ToString();
        }

        public ExactVersions(Version version, SemVersion packageVersion)
        {
            Version = version;
            SemVersion = PackageVersion = packageVersion;
            AssemblyVersion = AssemblyFileVersion = version;
            AssemblyInformationalVersion = packageVersion.ToString();
        }

        public Version Version { get; init; }

        public SemVersion SemVersion { get; init; }

        public Version AssemblyVersion { get; init; }

        public Version AssemblyFileVersion { get; init; }

        public string AssemblyInformationalVersion { get; init; }

        public SemVersion PackageVersion { get; init; }
    }
}
