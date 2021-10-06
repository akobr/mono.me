using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    public interface IOpStrategiesFactory
    {
        IOpStrategy<T> BuildStrategy<T>(IItem item, string operationKey);

        IOpStrategy BuildStrategy(IItem item, string operationKey);
    }
}
