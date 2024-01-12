using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace _42.Monorepo.Cli.Configuration
{
    public interface IItemOptionsProvider
    {
        bool TryGetOptions(string path, [MaybeNullWhen(false)] out ItemOptions? options);

        ItemOptions GetOptions(string path);

        IEnumerable<ItemOptions> GetAllOptions();
    }
}
