using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _42.Monorepo.Cli.Model.Records
{
    public class RootDirectoryRecord : Record, IRootDirectoryRecord
    {
        private readonly Lazy<IReadOnlyCollection<IWorksteadRecord>> worksteads;
        private readonly IRepositoryRecord _repository;

        public RootDirectoryRecord(string path, IRepositoryRecord parent)
            : base(path, parent)
        {
            _repository = parent;
            worksteads = new(CalculateWorksteads);
        }

        public override RecordType Type => RecordType.RootDirectory;

        public IReadOnlyCollection<IWorksteadRecord> GetWorksteads()
            => worksteads.Value;

        public IEnumerable<IProjectRecord> GetAllProjects()
            => GetWorksteads().SelectMany(w => w.GetAllProjects());

        private IReadOnlyCollection<IWorksteadRecord> CalculateWorksteads()
        {
            return Directory
                .GetDirectories(Path)
                .Where(dir => !MonorepoDirectoryFunctions.IsExcludedDirectory(dir))
                .Select(dir => new WorksteadRecord(dir, _repository))
                .ToList();
        }
    }
}
