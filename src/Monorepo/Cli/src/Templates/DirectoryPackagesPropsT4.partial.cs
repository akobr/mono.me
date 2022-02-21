using _42.Monorepo.Cli.Features;

namespace _42.Monorepo.Cli.Templates
{
    public partial class DirectoryPackagesPropsT4
    {
        private readonly IFeatureProvider _featureProvider;

        public DirectoryPackagesPropsT4(IFeatureProvider featureProvider)
        {
            _featureProvider = featureProvider;
        }
    }
}
