using System.Collections.Generic;
using System.Collections.Immutable;

namespace c0ded0c.Core.Configuration
{
    public class ToolConfigurationReader : ConfigurationSegmentReader, IToolConfiguration
    {
        public ToolConfigurationReader(IReadOnlyDictionary<string, string> configuration)
            : base(configuration)
        {
            // no operation
        }

        public string? WorkingDirectory => Get();

        public string? OutputDirectory => Get();

        public string? RunName => Get();

        public string? Version => Get();

        public bool IsPacked => GetBoolean();

        public bool IsIncremental => GetBoolean();

        public IEnumerable<string>? Modules => GetEnumerable();

        public IEnumerable<string>? Plugins => GetEnumerable();

        public IEnumerable<string>? InputPaths => GetEnumerable();

        public static IImmutableDictionary<string, string> SetTo(
            IImmutableDictionary<string, string> properties,
            IToolConfiguration configuration)
        {
            return SetTo<IToolConfiguration>(properties, configuration);
        }
    }
}
