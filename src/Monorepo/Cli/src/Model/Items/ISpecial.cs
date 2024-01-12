using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model.Items;

public interface ISpecial : IItem
{
    new ISpecialRecord Record { get; }
}
