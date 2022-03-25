using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration;

public interface IItemFullOptionsProvider
{
    IItemFullOption GetOptions(string path, string? defaultTypeKey = null);

    IEnumerable<IItemFullOption> GetAllOptions();
}
