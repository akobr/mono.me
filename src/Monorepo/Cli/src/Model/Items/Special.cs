using System;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items;

public class Special(ISpecialRecord record, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
    : Item(record, executor, itemFactory), ISpecial
{
    public new ISpecialRecord Record { get; } = record;
}
