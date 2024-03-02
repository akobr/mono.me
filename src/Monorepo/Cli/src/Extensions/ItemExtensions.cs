using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Extensions
{
    public static class ItemExtensions
    {
        public static IItem? TryGetConcreteItem(this IItem item, RecordType type)
        {
            var target = item;

            while (target?.Record.Type > type)
            {
                target = target.Parent;
            }

            return target?.Record.Type == type
                   || (type == RecordType.Workstead && target?.Record.Type == RecordType.TopWorkstead)
                ? target
                : null;
        }

        public static IRecord? TryGetConcreteItem(this IRecord record, RecordType type)
        {
            var target = record;

            while (target?.Type > type)
            {
                target = target.Parent;
            }

            return target?.Type == type
                   || (type == RecordType.Workstead && target?.Type == RecordType.TopWorkstead)
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

            foreach (var segment in relativeSegments.Skip(1))
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
            var type = record.Type == RecordType.TopWorkstead
                ? RecordType.Workstead
                : record.Type;

            return Enum.GetName(type) ?? string.Empty;
        }

        public static char GetTypeAsChar(this IRecord record)
        {
            var type = record.Type == RecordType.TopWorkstead
                ? RecordType.Workstead
                : record.Type;

            return char.ToLowerInvariant(Enum.GetName(type)?[0] ?? ' ');
        }

        public static async Task<string> GetFullNameAsync(this IItem @this, IItemFullOptionsProvider optionsProvider, MonoRepoOptions repoOptions)
        {
            switch (@this.Record.Type)
            {
                case RecordType.TopWorkstead:
                case RecordType.Workstead:
                    return GetWorksteadFullName(@this, optionsProvider, repoOptions);

                case RecordType.Project:
                    return await ((IProject)@this).GetPackageNameAsync();

                // case RecordType.Repository:
                // case RecordType.RootDirectory:
                // case RecordType.Special:
                default:
                    return @this.Record.Name;
            }
        }

        public static bool IsWorkstead(this RecordType type)
        {
            return type is RecordType.TopWorkstead or RecordType.Workstead;
        }

        private static string GetWorksteadFullName(this IItem @this, IItemFullOptionsProvider optionsProvider, MonoRepoOptions repoOptions)
        {
            var itemToProcess = @this;
            var builder = new StringBuilder();

            do
            {
                var options = optionsProvider.GetWorksteadOptions(itemToProcess.Record.RepoRelativePath);

                if (!options.IsSuppressed())
                {
                    if (builder.Length > 0)
                    {
                        builder.Insert(0, '.');
                    }

                    builder.Insert(0, itemToProcess.Record.Name);
                }

                itemToProcess = itemToProcess.Parent;
            }
            while (itemToProcess is IWorkstead);

            var prefix = (repoOptions.Prefix ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(prefix))
            {
                if (builder.Length > 0)
                {
                    builder.Insert(0, '.');
                }

                builder.Insert(0, prefix);
            }

            return builder.ToString();
        }
    }
}
