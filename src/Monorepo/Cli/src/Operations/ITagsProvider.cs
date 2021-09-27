using System.Collections.Generic;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public interface ITagsProvider
    {
        IReadOnlyCollection<Tag> GetTags();
    }
}
