using System;
using System.Collections.Generic;
using System.Linq;
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

        private FeatureProvider(IEnumerable<string> features)
        {
            _features = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsEnabled(string featureName)
        {
            lock (_locker)
            {
                return _features.Contains(featureName);
            }
        }

        public IEnumerable<string> GetAllEnabled(string featurePrefix)
        {
            lock (_locker)
            {
                return _features.Where(f => f.StartsWith(featurePrefix, StringComparison.OrdinalIgnoreCase));
            }
        }

        internal static FeatureProvider Build(IEnumerable<string> features)
        {
            return new FeatureProvider(features);
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
