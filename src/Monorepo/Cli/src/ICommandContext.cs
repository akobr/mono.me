using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli
{
    public interface ICommandContext
    {
        bool IsValid { get; }

        IItem Item { get; }

        IRepository Repository { get; }

        public void TryFailedIfInvalid();
    }
}
