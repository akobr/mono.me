using System;
using System.Collections.Generic;

namespace _42.Monorepo.Repo.Generator
{
    internal class WorkResults
    {
        public WorkStats Statistics { get; set; } = new();

        public IReadOnlyList<string> NewProjects { get; set; } = Array.Empty<string>();

        public string WorksteadName { get; set; } = string.Empty;
    }
}
