using System;
using System.IO;
using System.Linq;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Extensions
{
    public static class ItemExtensions
    {
        public static IItem? TryGetConcreteItem(this IItem item, ItemType type)
        {
            var target = item;

            while (target?.Record.Type > type)
            {
                target = target.Parent;
            }

            return target?.Record.Type == type
                   || (type == ItemType.Workstead && target?.Record.Type == ItemType.TopWorkstead)
                ? target
                : null;
        }

        public static IRecord? TryGetConcreteItem(this IRecord record, ItemType type)
        {
            var target = record;

            while (target?.Type > type)
            {
                target = target.Parent;
            }

            return target?.Type == type
                   || (type == ItemType.Workstead && target?.Type == ItemType.TopWorkstead)
                ? target
                : null;
        }

        public static IItem? TryGetDescendant(this IItem ancestor, IRecord record)
        {
            var ancestorRecord = ancestor.Record;
            if (!record.Path.StartsWith(ancestorRecord.Path, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relativePath = record.Path.GetRelativePath(ancestorRecord.Path);
            var relativeSegments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, 256);

            var item = ancestor;

            foreach (var segment in relativeSegments)
            {
                item = item.GetChildren().FirstOrDefault(i => i.Record.Identifier.Name.EqualsOrdinalIgnoreCase(segment));

                if (item is null)
                {
                    return null;
                }
            }

            return item;
        }

        public static string GetTypeAsString(this IRecord record)
        {
            var type = record.Type == ItemType.TopWorkstead
                ? ItemType.Workstead
                : record.Type;

            return Enum.GetName(type) ?? string.Empty;
        }

        public static bool IsWorkstead(this ItemType type)
        {
            return type is ItemType.TopWorkstead or ItemType.Workstead;
        }
    }
}
