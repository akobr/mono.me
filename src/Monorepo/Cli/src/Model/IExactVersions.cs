using System;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public interface IExactVersions
    {
        public Version Version { get; }

        public Version AssemblyVersion { get; }

        public string AssemblyInformationalVersion { get; }

        public SemVersion PackageVersion { get; }
    }
}
