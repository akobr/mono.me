using System;
using System.Collections.Generic;
using System.Linq;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items
{
    public class RootDirectory : Item, IRootDirectory
    {
        private readonly Lazy<IReadOnlyCollection<IWorkstead>> _worksteads;

        public RootDirectory(IRootDirectoryRecord record, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
            : base(record, executor, itemFactory)
        {
            Record = record;
            _worksteads = new(CalculateWorksteads, true);
        }

        public new IRootDirectoryRecord Record { get; }

        public override IEnumerable<IItem> GetChildren()
        {
            return GetWorksteads();
        }

        public IReadOnlyCollection<IWorkstead> GetWorksteads()
            => _worksteads.Value;

        private IReadOnlyCollection<IWorkstead> CalculateWorksteads()
        {
            return Record.GetWorksteads()
                .Select(w => (IWorkstead)ItemFactory(w))
                .ToList();
        }
    }
}
