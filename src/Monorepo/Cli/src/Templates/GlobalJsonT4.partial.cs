using _42.Monorepo.Cli.Features;

namespace _42.Monorepo.Cli.Templates
{
    public partial class GlobalJsonT4
    {
        private readonly IFeatureProvider _featureProvider;

        public GlobalJsonT4(IFeatureProvider featureProvider)
        {
            _featureProvider = featureProvider;
        }
    }
}
