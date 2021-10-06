namespace _42.Monorepo.Cli.Configuration
{
    public interface IItemOptionsProvider
    {
        ItemOptions TryGetOptions(string path);
    }
}
