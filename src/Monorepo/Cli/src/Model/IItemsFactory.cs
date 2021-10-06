using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model
{
    public interface IItemsFactory
    {
        IItem BuildItem(IRecord record);

        TPoweredItem BuildItem<TPoweredItem>(IRecord record)
            where TPoweredItem : IItem;
    }
}
