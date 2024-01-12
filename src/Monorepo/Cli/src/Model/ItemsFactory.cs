using System;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model
{
    public class ItemsFactory : IItemsFactory
    {
        private readonly IOpsExecutor executor;

        public ItemsFactory(IOpsExecutor executor)
        {
            this.executor = executor;
        }

        public IItem BuildItem(IRecord record)
        {
            return record switch
            {
                RepositoryRecord repository => new Repository(repository, executor, BuildItem),
                WorksteadRecord workstead => new Workstead(workstead, executor, BuildItem),
                ProjectRecord project => new Project(project, executor, BuildItem),
                SpecialRecord special => new Special(special, executor, BuildItem),
                InvalidRecord invalid => new InvalidItem(invalid),
                _ => throw new ArgumentOutOfRangeException(nameof(record), record, "An unsupported type of item."),
            };
        }

        public TPoweredItem BuildItem<TPoweredItem>(IRecord record)
            where TPoweredItem : IItem
        {
            return (TPoweredItem)BuildItem(record);
        }
    }
}
