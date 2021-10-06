using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace _42.Monorepo.Cli.Configuration
{
    public class ItemOptionsProvider : IItemOptionsProvider
    {
        private readonly ConcurrentDictionary<string, ItemOptions> options;

        public ItemOptionsProvider(IConfiguration configuration)
        {
            var items = configuration
                .GetSection(ConfigurationSections.Items)
                .Get<List<ItemOptions>>();

            options = new ConcurrentDictionary<string, ItemOptions>(
                items.Select(i => new KeyValuePair<string, ItemOptions>(i.Path, i)),
                StringComparer.OrdinalIgnoreCase);
        }

        public ItemOptions TryGetOptions(string path)
        {
            if (!options.TryGetValue(path, out var itemOptions))
            {
                return new ItemOptions();
            }

            return itemOptions;
        }
    }
}
