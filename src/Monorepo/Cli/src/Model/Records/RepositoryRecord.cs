using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _42.Monorepo.Cli.Extensions;

namespace _42.Monorepo.Cli.Model.Records
{
    public class RepositoryRecord : Record, IRepositoryRecord
    {
        private readonly IRootDirectoryRecord sourceDirectory;
        private readonly HashSet<IRootDirectoryRecord> rootDirectories;

        public RepositoryRecord(string path)
            : base(path, null)
        {
            sourceDirectory = new RootDirectoryRecord(
                System.IO.Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME),
                this);

            rootDirectories = new HashSet<IRootDirectoryRecord> { sourceDirectory };
        }

        public override RecordType Type => RecordType.Repository;

        public override string RepoRelativePath => ".";

        public bool IsValid
            => Directory
                .GetFiles(Path, Constants.MONOREPO_CONFIG_JSON, SearchOption.TopDirectoryOnly)
                .Any();

        public IReadOnlyCollection<IRootDirectoryRecord> GetDirectories()
            => rootDirectories;

        public IReadOnlyCollection<IWorksteadRecord> GetWorksteads()
            => sourceDirectory.GetWorksteads();

        public IEnumerable<IProjectRecord> GetAllProjects()
            => sourceDirectory.GetAllProjects();

        public void AddRootDirectory(string path)
        {
            var fullPath = System.IO.Path.GetFullPath(path);

            if (!fullPath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            rootDirectories.Add(new RootDirectoryRecord(fullPath, this));
        }
    }
}
