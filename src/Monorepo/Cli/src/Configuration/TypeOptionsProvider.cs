using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace _42.Monorepo.Cli.Configuration
{
    public class TypeOptionsProvider : ITypeOptionsProvider
    {
        private readonly Dictionary<string, TypeOptions> options;

        public TypeOptionsProvider(IConfiguration configuration)
        {
            options = new Dictionary<string, TypeOptions>();
            var section = configuration.GetSection(ConfigurationSections.TYPES);

            foreach (var typeSection in section.GetChildren())
            {
                var typeName = typeSection.Key;
                var typeOptions = typeSection.Get<TypeOptions>();
                typeOptions.Name = typeName;
                options.Add(typeName, typeOptions);
            }
        }

        public TypeOptions GetOptions(string typeName)
        {
            return options.TryGetValue(typeName, out var typeOptions)
                ? typeOptions
                : new TypeOptions { Name = typeName };
        }
    }
}
