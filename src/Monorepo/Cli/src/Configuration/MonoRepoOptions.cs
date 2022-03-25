using System;
using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration
{
    public class MonoRepoOptions
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Prefix { get; set; }

        public string[] Features { get; set; } = Array.Empty<string>();

        public Dictionary<string, string> Scripts { get; set; } = new();

        public string? Shell { get; set; }
    }
}
