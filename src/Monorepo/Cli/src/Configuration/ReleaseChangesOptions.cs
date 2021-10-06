using System;

namespace _42.Monorepo.Cli.Configuration
{
    public class ReleaseChangesOptions
    {
        public string[] Major { get; set; } = Array.Empty<string>();

        public string[] Minor { get; set; } = Array.Empty<string>();

        public string[] Patch { get; set; } = Array.Empty<string>();

        public string[] Harmless { get; set; } = Array.Empty<string>();
    }
}
