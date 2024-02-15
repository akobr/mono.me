using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

public abstract class BaseContextCommand : BaseCommand
{
    public BaseContextCommand(IExtendedConsole console, ICommandContext commandContext)
        : base(console)
    {
        Context = commandContext;
    }

    [Option("-p|--projectKey", CommandOptionType.SingleValue, Description = "Explicitly targeted project.")]
    public string? ProjectKey { get; }

    [Option("-v|--view", CommandOptionType.SingleValue, Description = "Explicitly targeted view.")]
    public string? ViewName { get; }

    protected ICommandContext Context { get; }

    public override async Task<int> OnExecuteAsync()
    {
        var resultCode = await ExecutePreconditionsAsync();

        if (resultCode.HasValue)
        {
            return resultCode.Value;
        }

        var exitCode = await ExecuteAsync();
        await ExecutePostconditionsAsync();
        return exitCode;
    }

    protected abstract Task<int> ExecuteAsync();

    protected virtual Task<int?> ExecutePreconditionsAsync()
    {
        Context.TrySetExplicitTarget(ProjectKey, ViewName);
        return Task.FromResult<int?>(null);
    }

    protected virtual Task ExecutePostconditionsAsync()
    {
        return Task.CompletedTask;
    }
}
