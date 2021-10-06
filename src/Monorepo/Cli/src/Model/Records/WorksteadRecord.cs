using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _42.Monorepo.Cli.Model.Records
{
    public class WorksteadRecord : Record, IWorksteadRecord
    {
        private readonly Lazy<IReadOnlyCollection<IWorksteadRecord>> worksteads;
        private readonly Lazy<IReadOnlyCollection<IProjectRecord>> projects;

        public WorksteadRecord(string path, IRecord parent)
            : base(path, parent)
        {
            worksteads = new(CalculateSubWorksteads);
            projects = new(CalculateProjects);
        }

        public override ItemType Type => IsTopLevel ? ItemType.TopWorkstead : ItemType.Workstead;

        public bool IsTopLevel => Parent is null || Parent.Type == ItemType.Repository;

        public IReadOnlyCollection<IWorksteadRecord> GetSubWorksteads() => worksteads.Value;

        public IReadOnlyCollection<IProjectRecord> GetProjects() => projects.Value;

        public IEnumerable<IProjectRecord> GetAllProjects()
            => GetSubWorksteads().SelectMany(w => w.GetAllProjects()).Concat(GetProjects());

        private IReadOnlyCollection<IWorksteadRecord> CalculateSubWorksteads()
        {
            return Directory.GetDirectories(Path)
                .Where(dir => !MonorepoDirectoryFunctions.IsExcludedDirectory(dir))
                .Where(dir => !MonorepoDirectoryFunctions.IsProjectDirectory(dir))
                .Select(dir => new WorksteadRecord(dir, this))
                .ToList();
        }

        private IReadOnlyCollection<IProjectRecord> CalculateProjects()
        {
            return Directory.GetDirectories(Path)
                .Where(dir => !MonorepoDirectoryFunctions.IsExcludedDirectory(dir))
                .Where(MonorepoDirectoryFunctions.IsProjectDirectory)
                .Select(dir => new ProjectRecord(dir, this))
                .ToList();
        }
    }
}
