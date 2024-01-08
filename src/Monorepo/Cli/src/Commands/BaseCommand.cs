using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;

namespace _42.Monorepo.Cli.Commands
{
    public abstract class BaseCommand : IAsyncCommand
    {
        protected BaseCommand(IExtendedConsole console, ICommandContext context)
        {
            Console = console;
            Context = context;
        }

        protected IExtendedConsole Console { get; }

        protected ICommandContext Context { get; }

        public async Task<int> OnExecuteAsync()
        {
            await ExecutePreconditionsAsync();
            var exitCode = await ExecuteAsync();
            await ExecutePostconditionsAsync();
            return exitCode;
        }

        protected abstract Task<int> ExecuteAsync();

        protected virtual Task ExecutePreconditionsAsync()
        {
            Context.TryFailedIfInvalid();
            return Task.CompletedTask;
        }

        protected virtual Task ExecutePostconditionsAsync()
        {
            return Task.CompletedTask;
        }
    }
}
