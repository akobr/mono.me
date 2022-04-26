using System.Collections.Generic;

namespace c0ded0c.Core.Configuration
{
    public class ToolConfiguration : IToolConfiguration
    {
        public string? OutputDirectory { get; set; }

        public string? WorkingDirectory { get; set; }

        public string? RunName { get; set; }

        public string? Version { get; set; }

        public bool IsPacked { get; set; }

        public bool IsIncremental { get; set; }

        public IEnumerable<string>? Modules { get; set; }

        public IEnumerable<string>? Plugins { get; set; }

        public IEnumerable<string>? InputPaths { get; set; }
    }
}
