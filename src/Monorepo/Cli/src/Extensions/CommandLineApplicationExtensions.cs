using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Extensions
{
    public static class CommandLineApplicationExtensions
    {
        public static Task<int> ExecuteByNameAsync(this IEnumerable<CommandLineApplication> commands, string commandName)
        {
            return commands.First(c => c.Name == commandName).ExecuteAsync(Array.Empty<string>());
        }

        public static void SetLongVersionGetter(this CommandLineApplication<MonorepoCommand> application)
        {
            application.LongVersionGetter = GetApplicationVersion;
        }

        private static string GetApplicationVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            var informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var result = new StringBuilder();

            if (fileVersionAttribute is not null)
            {
                result.Append(fileVersionAttribute.Version);
            }
            else
            {
                result.Append(assembly.GetName().Version?.ToString() ?? "42-the-answer");
            }

            if (informationalVersionAttribute is not null)
            {
                result.Append($" ({informationalVersionAttribute.InformationalVersion})");
            }

            return result.ToString();
        }
    }
}
