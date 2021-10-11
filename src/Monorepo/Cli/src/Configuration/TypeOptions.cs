using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration
{
    public class TypeOptions
    {
        public string Name { get; set; } = string.Empty;

        public Dictionary<string, string> Custom { get; set; } = new();

        public Dictionary<string, string> Scripts { get; set; } = new();

    }
}
