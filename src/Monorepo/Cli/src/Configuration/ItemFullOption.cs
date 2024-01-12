using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using _42.Monorepo.Cli.Extensions;

namespace _42.Monorepo.Cli.Configuration;

[DebuggerDisplay("{Path}")]
public class ItemFullOption : IItemFullOption
{
    private readonly ItemOptions _item;
    private readonly TypeOptions _type;

    public ItemFullOption(ItemOptions itemOptions, TypeOptions typeOptions)
    {
        _item = itemOptions;
        _type = typeOptions;

        Exclude = new HashSet<string>(_item.Exclude.Concat(typeOptions.Exclude));
        Scripts = DictionaryExtensions.MergeLeft(_item.Scripts, typeOptions.Scripts);
        Custom = DictionaryExtensions.MergeLeft(_item.Custom, typeOptions.Custom);
    }

    public string Path => _item.Path;

    public string? Name => _item.Name;

    public string? Description => _item.Description;

    public string Type => _type.Key;

    public IReadOnlyCollection<string> Dependencies => _item.Dependencies;

    public IReadOnlyCollection<string> Exclude { get; }

    public IReadOnlyDictionary<string, string> Scripts { get; }

    public IReadOnlyDictionary<string, string> Custom { get; }
}
