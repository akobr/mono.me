using System;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Extensions
{
    public static class ItemExtensions
    {
        public static IItem? TryGetConcreteItem(this IItem item, ItemType type)
        {
            var target = item;

            while (item?.Record.Type > type)
            {
                target = item.Parent;
            }

            return target?.Record.Type == type
                   || (type == ItemType.Workstead && target?.Record.Type == ItemType.TopWorkstead)
                ? target
                : null;
        }

        public static IItemRecord? TryGetConcreteItem(this IItemRecord record, ItemType type)
        {
            var target = record;

            while (record?.Type > type)
            {
                target = record.Parent;
            }

            return target?.Type == type
                   || (type == ItemType.Workstead && target?.Type == ItemType.TopWorkstead)
                ? target
                : null;
        }
    }
}
