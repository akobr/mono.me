using _42.Monorepo.Cli.Features;

namespace _42.Monorepo.Cli.Templates
{
    public partial class ProjectTestCsprojT4
    {
        private readonly IFeatureProvider _featureProvider;
        private readonly string _testsType;
        private readonly int _dotNetVersion;

        public ProjectTestCsprojT4(IFeatureProvider featureProvider, string testType, int dotNetVersion)
        {
            _featureProvider = featureProvider;
            _testsType = testType;
            _dotNetVersion = dotNetVersion;
        }
    }
}
