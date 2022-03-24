using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Configuration;

public static class ItemFullOptionsProviderExtensions
{
    public static IItemFullOption GetProjectOptions(this IItemFullOptionsProvider @this, string path)
    {
        return @this.GetOptions(path, TypeKeys.DOTNET_PROJECT);
    }

    public static IItemFullOption GetWorksteadOptions(this IItemFullOptionsProvider @this, string path)
    {
        return @this.GetOptions(path, TypeKeys.DOTNET_WORKSTEAD);
    }

    public static IItemFullOption GetItemOptions(this IItemFullOptionsProvider @this, IRecord record)
    {
        return @this.GetOptions(record.RepoRelativePath, GetDefaultOptionType(record));
    }

    private static string? GetDefaultOptionType(IRecord record)
    {
        return record.Type switch
        {
            Model.RecordType.Workstead => TypeKeys.DOTNET_WORKSTEAD,
            Model.RecordType.Project => TypeKeys.DOTNET_PROJECT,
            _ => null,
        };
    }
}
