using System.Collections.Generic;

namespace c0ded0c.Core.Configuration
{
    public interface IToolConfiguration
    {
        string? OutputDirectory { get; }

        string? WorkingDirectory { get; }

        string? RunName { get; }

        string? Version { get; }

        bool IsPacked { get; }

        bool IsIncremental { get; }

        IEnumerable<string>? Modules { get; }

        IEnumerable<string>? Plugins { get; }

        IEnumerable<string>? InputPaths { get; }
    }
}
