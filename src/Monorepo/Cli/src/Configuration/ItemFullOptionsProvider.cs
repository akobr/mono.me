namespace _42.Monorepo.Cli.Configuration;

public class ItemFullOptionsProvider : IItemFullOptionsProvider
{
    private readonly IItemOptionsProvider _itemOptionsProvider;
    private readonly ITypeOptionsProvider _typeOptionsProvider;

    public ItemFullOptionsProvider(
        IItemOptionsProvider itemOptionsProvider,
        ITypeOptionsProvider typeOptionsProvider)
    {
        _itemOptionsProvider = itemOptionsProvider;
        _typeOptionsProvider = typeOptionsProvider;
    }

    public IItemFullOption GetOptions(string path, string defaultTypeKey = TypeKeys.UNKNOWN)
    {
        var itemOptions = _itemOptionsProvider.GetOptions(path);
        var typeOptionsKey = itemOptions.Type ?? defaultTypeKey;
        var typeOptions = _typeOptionsProvider.GetOptions(typeOptionsKey);

        return new ItemFullOption(itemOptions, typeOptions);
    }
}
