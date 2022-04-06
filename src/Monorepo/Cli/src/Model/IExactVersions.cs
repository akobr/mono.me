using System;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public interface IExactVersions
    {
        public Version Version { get; }

        public SemVersion SemVersion { get; }

        public Version AssemblyVersion { get; }

        public Version AssemblyFileVersion { get; }

        public string AssemblyInformationalVersion { get; }

        public SemVersion PackageVersion { get; }
    }
}
