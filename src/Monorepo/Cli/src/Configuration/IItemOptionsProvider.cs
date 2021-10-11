using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration
{
    public interface IItemOptionsProvider
    {
        ItemOptions GetOptions(string path);

        IEnumerable<ItemOptions> GetAllOptions();
    }
}
