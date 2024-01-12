using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace _42.Monorepo.Cli.Configuration
{
    public class ItemOptionsProvider : IItemOptionsProvider
    {
        private readonly Dictionary<string, ItemOptions> _options;

        public ItemOptionsProvider(IConfiguration configuration)
        {
            var items = configuration
                .GetSection(ConfigurationSections.ITEMS)
                .Get<List<ItemOptions>>();

            if (items is null
                || items.Count < 1)
            {
                _options = new Dictionary<string, ItemOptions>();
                return;
            }

            _options = new Dictionary<string, ItemOptions>(
                items.Select(i => new KeyValuePair<string, ItemOptions>(i.Path, i)),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGetOptions(string path, [MaybeNullWhen(false)] out ItemOptions? options)
        {
            return _options.TryGetValue(path, out options);
        }

        public ItemOptions GetOptions(string path)
        {
            return _options.TryGetValue(path, out var itemOptions)
                ? itemOptions
                : new ItemOptions { Path = path };
        }

        public IEnumerable<ItemOptions> GetAllOptions()
        {
            return _options.Values;
        }
    }
}
