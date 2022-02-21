namespace _42.Monorepo.Cli.Features
{
    public interface IFeatureProvider
    {
        bool IsEnabled(string featureName);
    }
}
