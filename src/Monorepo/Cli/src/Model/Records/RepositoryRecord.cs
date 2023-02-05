using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _42.Monorepo.Cli.Model.Records
{
    public class RepositoryRecord : Record, IRepositoryRecord
    {
        private readonly IRootDirectoryRecord _sourceDirectory;
        private readonly HashSet<IRootDirectoryRecord> _rootDirectories;

        public RepositoryRecord(string path)
            : base(path, null)
        {
            _sourceDirectory = new RootDirectoryRecord(
                System.IO.Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME),
                this);

            _rootDirectories = new HashSet<IRootDirectoryRecord> { _sourceDirectory };
        }

        public override RecordType Type => RecordType.Repository;

        public override string RepoRelativePath => ".";

        public bool IsValid
            => Directory
                .GetFiles(Path, Constants.MONOREPO_CONFIG_JSON, SearchOption.TopDirectoryOnly)
                .Any();

        public IReadOnlyCollection<IRootDirectoryRecord> GetDirectories()
            => _rootDirectories;

        public IReadOnlyCollection<IWorksteadRecord> GetWorksteads()
            => _sourceDirectory.GetWorksteads();

        public IEnumerable<IProjectRecord> GetAllProjects()
            => _sourceDirectory.GetAllProjects();

        public void AddRootDirectory(string path)
        {
            var fullPath = System.IO.Path.GetFullPath(path);

            if (!fullPath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _rootDirectories.Add(new RootDirectoryRecord(fullPath, this));
        }
    }
}
