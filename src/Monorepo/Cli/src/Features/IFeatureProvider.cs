using System.Collections.Generic;

namespace _42.Monorepo.Cli.Features
{
    public interface IFeatureProvider
    {
        bool IsEnabled(string featureName);

        IEnumerable<string> GetAllEnabled(string featurePrefix);
    }
}
