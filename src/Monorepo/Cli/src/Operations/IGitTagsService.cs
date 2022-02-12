using System.Collections.Generic;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public interface IGitTagsService
    {
        IReadOnlyCollection<Tag> GetTags();
    }
}
