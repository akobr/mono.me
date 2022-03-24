using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli
{
    public interface ICommandContext
    {
        bool IsValid { get; }

        IItem Item { get; }

        IRepository Repository { get; }

        void ReInitialize(string repoRelativePath);

        void TryFailedIfInvalid();
    }
}
