using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model
{
    public interface IItemsFactory
    {
        IItem BuildItem(IItemRecord record);

        TPoweredItem BuildItem<TPoweredItem>(IItemRecord record)
            where TPoweredItem : IItem;
    }
}
