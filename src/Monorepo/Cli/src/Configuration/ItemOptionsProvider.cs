using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace _42.Monorepo.Cli.Configuration
{
    public class ItemOptionsProvider : IItemOptionsProvider
    {
        private readonly Dictionary<string, ItemOptions> options;

        public ItemOptionsProvider(IConfiguration configuration)
        {
            var items = configuration
                .GetSection(ConfigurationSections.ITEMS)
                .Get<List<ItemOptions>>();

            options = new Dictionary<string, ItemOptions>(
                items.Select(i => new KeyValuePair<string, ItemOptions>(i.Path, i)),
                StringComparer.OrdinalIgnoreCase);
        }

        public ItemOptions GetOptions(string path)
        {
            return options.TryGetValue(path, out var itemOptions)
                ? itemOptions
                : new ItemOptions { Path = path };
        }

        public IEnumerable<ItemOptions> GetAllOptions()
        {
            return options.Values;
        }
    }
}
