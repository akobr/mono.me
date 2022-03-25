using System.Collections.Generic;

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

    public IItemFullOption GetOptions(string path, string? defaultTypeKey = null)
    {
        var itemOptions = _itemOptionsProvider.GetOptions(path);
        var typeOptionsKey = itemOptions.Type ?? defaultTypeKey;
        var typeOptions = _typeOptionsProvider.GetOptions(typeOptionsKey);
        return new ItemFullOption(itemOptions, typeOptions);
    }

    public IEnumerable<IItemFullOption> GetAllOptions()
    {
        foreach (var itemOptions in _itemOptionsProvider.GetAllOptions())
        {
            var typeOptions = _typeOptionsProvider.GetOptions(null);
            yield return new ItemFullOption(itemOptions, typeOptions);
        }
    }
}
