using System;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Versioning;
using Semver;

namespace _42.Monorepo.Cli.Extensions
{
    public static class ConsoleExtensions
    {
        public static SemVersion AskForVersion(this IExtendedConsole @this, string message, SemVersion defaultVersion)
        {
            var versionInput = @this.Input<string>(message, $"{defaultVersion}");

            if (!SemVersion.TryParse(versionInput, out var newVersion))
            {
                throw new InvalidOperationException("A version needs to be a valid semantic version.");
            }

            return newVersion;
        }

        public static VersionTemplate AskForVersionTemplate(this IExtendedConsole @this, string message, string defaultTemplate)
        {
            var templateInput = @this.Input<string>(message, $"{defaultTemplate}");

            if (!VersionTemplate.TryParse(templateInput, out var newTemplate))
            {
                throw new InvalidOperationException("A template needs to be a valid semantic version.");
            }

            return newTemplate;
        }
    }
}
