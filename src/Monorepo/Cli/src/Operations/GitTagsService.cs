using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public class GitTagsService : IGitTagsService
    {
        private readonly IGitRepositoryService _gitRepositoryService;
        private readonly Lazy<IReadOnlyCollection<Tag>> _tags;

        public GitTagsService(IGitRepositoryService gitRepositoryService)
        {
            _gitRepositoryService = gitRepositoryService;
            _tags = new Lazy<IReadOnlyCollection<Tag>>(LoadTags);
        }

        public IReadOnlyCollection<Tag> GetTags() => _tags.Value;

        private IReadOnlyCollection<Tag> LoadTags()
        {
            using var repo = _gitRepositoryService.BuildRepository();
            return repo.Tags.ToList();
        }
    }
}
