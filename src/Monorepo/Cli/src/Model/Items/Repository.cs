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

        public Repository(IRepositoryRecord record, IOperationsCache cache, Func<IItemRecord, IItem> itemFactory)
            : base(record, cache, itemFactory)
        {
            Record = record;

            worksteads = new(CalculateWorksteads, true);
        }

        public new IRepositoryRecord Record { get; }

        public IReadOnlyCollection<IWorkstead> GetWorksteads()
            => worksteads.Value;

        private IReadOnlyCollection<IWorkstead> CalculateWorksteads()
        {
            return Record.GetWorksteads()
                .Select(w => (IWorkstead)ItemFactory(w)).ToList();
        }
    }
}
