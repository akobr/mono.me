using System.Diagnostics.CodeAnalysis;
using _42.Monorepo.Cli.Extensions;
using Semver;

namespace _42.Monorepo.Cli.Versioning
{
    public class VersionTemplate : IVersionTemplate
    {
        public VersionTemplate(string template)
        {
            Template = template;
            Version = SemVersion.Parse(template);
        }

        public VersionTemplate(SemVersion version)
        {
            Template = version.ToTemplate();
            Version = version.Change();
        }

        public VersionTemplate(string template, SemVersion version)
        {
            Template = template;
            Version = version.Change();
        }

        public string Template { get; }

        public SemVersion Version { get; }

        public static bool TryParse(string template, [MaybeNullWhen(false)] out VersionTemplate model)
        {
            if (!SemVersion.TryParse(template, out var version))
            {
                model = null;
                return false;
            }

            model = new VersionTemplate(template, version);
            return true;
        }
    }
}
