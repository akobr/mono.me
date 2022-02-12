using System;
using System.Collections.Generic;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items
{
    public class RootDirectory : Item, IRootDirectory
    {
        public RootDirectory(IRootDirectoryRecord record, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
            : base(record, executor, itemFactory)
        {
            Record = record;
        }

        public new IRootDirectoryRecord Record { get; }

        public override IEnumerable<IItem> GetChildren()
        {
            return base.GetChildren();
        }
    }
}
