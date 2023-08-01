using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.CLI.Toolkit
{
    public abstract class BaseParentCommand : BaseCommand
    {
        private readonly CommandLineApplication _application;

        protected BaseParentCommand(IExtendedConsole console, CommandLineApplication application)
            : base(console)
        {
            _application = application;
        }

        public Task<int> ExecuteAsync()
        {
            return SelectSubCommandAndExecuteAsync(_application, Console);
        }

        internal static Task<int> SelectSubCommandAndExecuteAsync(CommandLineApplication application, IExtendedConsole console)
        {
            var options = new SelectOptions<string>
            {
                Items = application.Commands
                    .Where(c => !string.IsNullOrEmpty(c.Name))
                    .Select(c => c.Name!),
                Message = $"Which {application.Name} command you want to execute",
            };

            var commandName = console.Select(options);
            return application.Commands.ExecuteByNameAsync(commandName);
        }
    }
}
