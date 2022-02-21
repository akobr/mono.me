using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using _42.Monorepo.Cli.Configuration;
using Microsoft.Extensions.Options;

namespace _42.Monorepo.Cli.Features
{
    public class FeatureProvider : IFeatureProvider
    {
        private readonly HashSet<string> _features;
        private readonly object _locker = new object();

        public FeatureProvider(IOptionsMonitor<MonoRepoOptions> options)
        {
            _features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SetRepoOptions(options.CurrentValue, string.Empty);
            options.OnChange(SetRepoOptions);
        }

        public bool IsEnabled(string featureName)
        {
            lock (_locker)
            {
                return _features.Contains(featureName);
            }
        }

        private void SetRepoOptions(MonoRepoOptions options, string optionName)
        {
            lock (_locker)
            {
                _features.Clear();
                _features.UnionWith(options.Features);
            }
        }
    }
}
