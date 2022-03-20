using System;

namespace _42.Monorepo.Cli.Configuration
{
    public class ReleaseOptions
    {
        public string[] Branches { get; set; } = Array.Empty<string>();

        public ReleaseChangesOptions Changes { get; set; } = new ReleaseChangesOptions();

        public bool CreateReleaseBranch { get; set; }

        public string? IssueUrlTemplate { get; set; }
    }
}
