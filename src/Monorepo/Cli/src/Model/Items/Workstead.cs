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

        public Workstead(IWorksteadRecord record, IOperationsCache cache, Func<IItemRecord, IItem> itemFactory)
            : base(record, cache, itemFactory)
        {
            Record = record;

            worksteads = new(CalculateSubWorksteads, true);
            projects = new(CalculateProjects, true);
        }

        public new IWorksteadRecord Record { get; }

        public IReadOnlyCollection<IWorkstead> GetSubWorksteads()
            => worksteads.Value;

        public IReadOnlyCollection<IProject> GetSubProjects()
            => projects.Value;

        private IReadOnlyCollection<IWorkstead> CalculateSubWorksteads()
        {
            return Record.GetSubWorksteads()
                .Select(w => (IWorkstead)ItemFactory(w)).ToList();
        }

        private IReadOnlyCollection<IProject> CalculateProjects()
        {
            return Record.GetSubWorksteads()
                .Select(w => (IProject)ItemFactory(w)).ToList();
        }
    }
}
