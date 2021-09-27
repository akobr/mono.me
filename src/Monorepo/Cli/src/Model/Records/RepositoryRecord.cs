using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _42.Monorepo.Cli.Model.Records
{
    public class RepositoryRecord : ItemRecord, IRepositoryRecord
    {
        private readonly Lazy<IReadOnlyCollection<IWorksteadRecord>> worksteads;

        public RepositoryRecord(string path)
            : base(path, null)
        {
            worksteads = new(CalculateWorksteads);
        }

        public override ItemType Type => ItemType.Repository;

        public bool IsValid
            => Directory
                .GetFiles(Path, Constants.MONOREPO_CONFIG_JSON, SearchOption.TopDirectoryOnly)
                .Any();

        public IReadOnlyCollection<IWorksteadRecord> GetWorksteads() => worksteads.Value;

        private IReadOnlyCollection<IWorksteadRecord> CalculateWorksteads()
        {
            return Directory
                .GetDirectories(System.IO.Path.Combine(Path, Constants.SOURCE_DIRECTORY_NAME))
                .Where(dir => !MonorepoDirectoryFunctions.IsExcludedDirectory(dir))
                .Select(dir => new WorksteadRecord(dir, this))
                .ToList();
        }
    }
}
