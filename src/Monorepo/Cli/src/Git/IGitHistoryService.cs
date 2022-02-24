﻿using System.Collections.Generic;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Git
{
    public interface IGitHistoryService
    {
        GitHistoryReport GetHistory(string targetedRepoRelativePath, IReadOnlyCollection<Commit> commitsToStop);
    }

}