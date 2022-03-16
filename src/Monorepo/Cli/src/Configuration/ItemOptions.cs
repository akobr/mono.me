using System;
using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration
{
    public class ItemOptions : IBasicOptions
    {
        public string Path { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Type { get; set; }

        public string[] Dependencies { get; set; } = Array.Empty<string>();

        public string[] Exclude { get; set; } = Array.Empty<string>();

        public Dictionary<string, string> Scripts { get; set; } = new();

        public Dictionary<string, string> Custom { get; set; } = new();

        IReadOnlyDictionary<string, string> IBasicOptions.Custom => Custom;

        IReadOnlyCollection<string> IBasicOptions.Exclude => Exclude;

        IReadOnlyDictionary<string, string> IBasicOptions.Scripts => Scripts;
    }
}
