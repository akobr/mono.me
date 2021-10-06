using System;

namespace _42.Monorepo.Cli.Configuration
{
    public class ItemOptions
    {
        public string Path { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? WorksteadType { get; set; }

        public string? ProjectType { get; set; }

        public string[] Dependencies { get; set; } = Array.Empty<string>();

        public string[] Exclude { get; set; } = Array.Empty<string>();
    }
}
