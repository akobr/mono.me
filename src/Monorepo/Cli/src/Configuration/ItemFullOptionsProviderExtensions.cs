namespace _42.Monorepo.Cli.Configuration;

public static class ItemFullOptionsProviderExtensions
{
    public static IItemFullOption GetProjectOptions(this IItemFullOptionsProvider @this, string path)
    {
        return @this.GetOptions(path, TypeKeys.DOTNET_PROJECT);
    }

    public static IItemFullOption GetWorksteadOptions(this IItemFullOptionsProvider @this, string path)
    {
        return @this.GetOptions(path, TypeKeys.DEFAULT_WORKSTEAD);
    }
}
