using _42.Monorepo.Cli.Features;

namespace _42.Monorepo.Cli.Templates
{
    public partial class RootDirectoryPackagesPropsT4
    {
        private readonly IFeatureProvider _featureProvider;

        public RootDirectoryPackagesPropsT4(IFeatureProvider featureProvider)
        {
            _featureProvider = featureProvider;
        }
    }
}
