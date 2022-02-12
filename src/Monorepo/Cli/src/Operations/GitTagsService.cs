using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public class GitTagsService : IGitTagsService
    {
        private readonly IGitRepositoryService gitRepositoryService;
        private readonly Lazy<IReadOnlyCollection<Tag>> tags;

        public GitTagsService(IGitRepositoryService gitRepositoryService)
        {
            this.gitRepositoryService = gitRepositoryService;
            tags = new Lazy<IReadOnlyCollection<Tag>>(LoadTags);
        }

        public IReadOnlyCollection<Tag> GetTags() => tags.Value;

        private IReadOnlyCollection<Tag> LoadTags()
        {
            using Repository repo = gitRepositoryService.BuildRepository();
            return repo.Tags.ToList();
        }
    }
}
