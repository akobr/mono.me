using _42.Monorepo.Cli.Features;

namespace _42.Monorepo.Cli.Templates
{
    public partial class ProjectTestCsprojT4
    {
        private readonly IFeatureProvider _featureProvider;
        private readonly string _testsType;

        public ProjectTestCsprojT4(IFeatureProvider featureProvider, string testType)
        {
            _featureProvider = featureProvider;
            _testsType = testType;
        }
    }
}
