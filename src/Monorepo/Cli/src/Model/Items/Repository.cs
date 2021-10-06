using System;
using System.Collections.Generic;
using System.Linq;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items
{
    public class Repository : Item, IRepository
    {
        private readonly Lazy<IReadOnlyCollection<IWorkstead>> worksteads;

        public Repository(IRepositoryRecord record, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
            : base(record, executor, itemFactory)
        {
            Record = record;
            worksteads = new(CalculateWorksteads, true);
        }

        public new IRepositoryRecord Record { get; }

        public override IEnumerable<IItem> GetChildren()
        {
            return GetWorksteads();
        }

        public IReadOnlyCollection<IWorkstead> GetWorksteads()
            => worksteads.Value;

        public IEnumerable<IProject> GetAllProjects()
            => GetWorksteads().SelectMany(w => w.GetAllProjects());

        private IReadOnlyCollection<IWorkstead> CalculateWorksteads()
        {
            return Record.GetWorksteads()
                .Select(w => (IWorkstead)ItemFactory(w)).ToList();
        }
    }
}
