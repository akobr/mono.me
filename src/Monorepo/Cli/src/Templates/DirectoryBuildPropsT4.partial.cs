using _42.Monorepo.Cli.Features;

namespace _42.Monorepo.Cli.Templates
{
    public partial class DirectoryBuildPropsT4
    {
        private readonly IFeatureProvider _featureProvider;

        public DirectoryBuildPropsT4(IFeatureProvider featureProvider)
        {
            _featureProvider = featureProvider;
        }
    }
}
