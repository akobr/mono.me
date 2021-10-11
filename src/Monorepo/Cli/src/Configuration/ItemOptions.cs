using System;
using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration
{
    public class ItemOptions
    {
        public string Path { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Type { get; set; }

        public string[] Dependencies { get; set; } = Array.Empty<string>();

        public string[] Exclude { get; set; } = Array.Empty<string>();

        public Dictionary<string, string> Scripts { get; set; } = new();
    }
}
