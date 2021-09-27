using System;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model
{
    public class ItemsFactory : IItemsFactory
    {
        private readonly IOperationsCache cache;

        public ItemsFactory(IOperationsCache cache)
        {
            this.cache = cache;
        }

        public IItem BuildItem(IItemRecord record)
        {
            return record switch
            {
                RepositoryRecord repository => new Item(repository, cache, BuildItem),
                WorksteadRecord workstead => new Item(workstead, cache, BuildItem),
                ProjectRecord project => new Project(project, cache, BuildItem),
                _ => throw new ArgumentOutOfRangeException(nameof(record), "Invalid item or unsupported type of item."),
            };
        }

        public TPoweredItem BuildItem<TPoweredItem>(IItemRecord record)
            where TPoweredItem : IItem
        {
            return (TPoweredItem)BuildItem(record);
        }
    }
}
