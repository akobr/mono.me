using System;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items
{
    internal class InvalidItem : Item
    {
        public InvalidItem(InvalidRecord record)
            : base(
                record,
                new EmptyOpsExecutor(),
                (_) => throw new InvalidOperationException("An invalid item has been used."))
        {
            // no opertion
        }
    }
}
