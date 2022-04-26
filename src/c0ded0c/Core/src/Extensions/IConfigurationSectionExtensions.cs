using Microsoft.Extensions.Configuration;

namespace c0ded0c.Core
{
    public static class IConfigurationSectionExtensions
    {
        public static string GetFullKey(this IConfigurationSection section)
        {
            string path = section.Path;
            return string.IsNullOrWhiteSpace(path)
                ? section.Key
                : $"{path.Replace(':', '.')}.{section.Key}";
        }
    }
}
