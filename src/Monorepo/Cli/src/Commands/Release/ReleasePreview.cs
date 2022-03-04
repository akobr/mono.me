using System;
using System.Collections.Generic;
using _42.Monorepo.Cli.ConventionalCommits;
using _42.Monorepo.Cli.Model;
using LibGit2Sharp;
using Semver;

namespace _42.Monorepo.Cli.Commands.Release
{
    public class ReleasePreview
    {
        public SemVersion Version { get; set; } = new(1);

        public string Tag { get; set; } = string.Empty;

        public string Branch { get; set; } = string.Empty;

        public string NotesRepoPath { get; set; } = string.Empty;

        public IRelease? PreviousRelease { get; set; }

        public SemVersion CurrentVersion { get; set; } = new(1);

        public string VersionFileFullPath { get; set; } = string.Empty;

        public IReadOnlyList<(string Project, SemVersion Version)> ProjectsToRelease { get; set; } = Array.Empty<(string, SemVersion)>();

        public IReadOnlyList<IConventionalMessage> MajorChanges { get; set; } = Array.Empty<IConventionalMessage>();

        public IReadOnlyList<IConventionalMessage> MinorChanges { get; set; } = Array.Empty<IConventionalMessage>();

        public IReadOnlyList<IConventionalMessage> PathChanges { get; set; } = Array.Empty<IConventionalMessage>();

        public IReadOnlyList<IConventionalMessage> HarmlessChanges { get; set; } = Array.Empty<IConventionalMessage>();

        public IReadOnlyList<Commit> UnknownChanges { get; set; } = Array.Empty<Commit>();
    }
}
