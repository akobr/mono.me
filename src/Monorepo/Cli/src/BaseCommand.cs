using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;

namespace _42.Monorepo.Cli
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

        public async Task OnExecuteAsync()
        {
            await ExecutePreconditionsAsync();
            await ExecuteAsync();
            await ExecutePostconditionsAsync();
        }

        protected abstract Task ExecuteAsync();

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
