using System;
using System.Collections.Generic;
using System.Linq;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items
{
    public class Workstead : Item, IWorkstead
    {
        private readonly Lazy<IReadOnlyCollection<IWorkstead>> worksteads;
        private readonly Lazy<IReadOnlyCollection<IProject>> projects;

        public Workstead(IWorksteadRecord record, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
            : base(record, executor, itemFactory)
        {
            Record = record;
            worksteads = new(CalculateSubWorksteads, true);
            projects = new(CalculateProjects, true);
        }

        public new IWorksteadRecord Record { get; }

        public override IEnumerable<IItem> GetChildren()
        {
            return GetSubWorksteads().Concat<IItem>(GetProjects());
        }

        public IReadOnlyCollection<IWorkstead> GetSubWorksteads()
            => worksteads.Value;

        public IReadOnlyCollection<IProject> GetProjects()
            => projects.Value;

        public IEnumerable<IProject> GetAllProjects()
            => GetSubWorksteads().SelectMany(w => w.GetAllProjects()).Concat(GetProjects());

        private IReadOnlyCollection<IWorkstead> CalculateSubWorksteads()
        {
            return Record.GetSubWorksteads()
                .Select(w => (IWorkstead)ItemFactory(w)).ToList();
        }

        private IReadOnlyCollection<IProject> CalculateProjects()
        {
            return Record.GetProjects()
                .Select(w => (IProject)ItemFactory(w)).ToList();
        }
    }
}
