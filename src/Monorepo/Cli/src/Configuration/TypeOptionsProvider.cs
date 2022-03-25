using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace _42.Monorepo.Cli.Configuration
{
    public class TypeOptionsProvider : ITypeOptionsProvider
    {
        private readonly Dictionary<string, TypeOptions> _options;

        public TypeOptionsProvider(IConfiguration configuration)
        {
            _options = new Dictionary<string, TypeOptions>();
            var section = configuration.GetSection(ConfigurationSections.TYPES);

            foreach (var typeSection in section.GetChildren())
            {
                var typeName = typeSection.Key;
                var typeOptions = typeSection.Get<TypeOptions>();
                typeOptions.Key = typeName;
                _options.Add(typeName, typeOptions);
            }
        }

        public TypeOptions GetOptions(string? typeKey)
        {
            return !string.IsNullOrWhiteSpace(typeKey)
                && _options.TryGetValue(typeKey, out var typeOptions)
                    ? typeOptions
                    : new TypeOptions { Key = typeKey ?? string.Empty };
        }

        public IEnumerable<TypeOptions> GetAllOptions()
        {
            return _options.Values;
        }
    }
}
