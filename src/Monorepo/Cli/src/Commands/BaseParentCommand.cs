using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Monorepo.Cli.Commands
{
    public abstract class BaseParentCommand : BaseCommand
    {
        private readonly CommandLineApplication application;

        protected BaseParentCommand(IExtendedConsole console, ICommandContext context, CommandLineApplication application)
            : base(console, context)
        {
            this.application = application;
        }

        protected override Task<int> ExecuteAsync()
        {
            var options = new SelectOptions<string>
            {
                Items = application.Commands
                    .Where(c => !string.IsNullOrEmpty(c.Name))
                    .Select(c => c.Name!),
                Message = $"Which {application.Name} command you want to execute",
            };

            var commandName = Console.Select(options);
            return application.Commands.ExecuteByNameAsync(commandName);
        }
    }
}
