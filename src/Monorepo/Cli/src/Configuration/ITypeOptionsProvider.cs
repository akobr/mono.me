using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration
{
    public interface ITypeOptionsProvider
    {
        TypeOptions GetOptions(string typeKey);

        IEnumerable<TypeOptions> GetAllOptions();
    }
}
