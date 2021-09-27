using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public class TagsProvider : ITagsProvider
    {
        private readonly IGitRepositoryFactory gitRepositoryFactory;
        private readonly Lazy<IReadOnlyCollection<Tag>> tags;

        public TagsProvider(IGitRepositoryFactory gitRepositoryFactory)
        {
            this.gitRepositoryFactory = gitRepositoryFactory;
            tags = new Lazy<IReadOnlyCollection<Tag>>(LoadTags);
        }

        public IReadOnlyCollection<Tag> GetTags() => tags.Value;

        private IReadOnlyCollection<Tag> LoadTags()
        {
            using LibGit2Sharp.Repository repo = gitRepositoryFactory.BuildRepository();
            return repo.Tags.ToList();
        }
    }
}
