using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

public abstract class BaseContextCommand : BaseCommand
{
    public BaseContextCommand(IExtendedConsole console, ICommandContext context)
        : base(console)
    {
        Context = context;
    }

    [Option("-p|--projectKey", CommandOptionType.SingleValue, Description = "Explicitly targeted organization and project. Use full key in format: <organizationName>.<projectName>")]
    public string? ProjectKey { get; }

    [Option("-v|--view", CommandOptionType.SingleValue, Description = "Explicitly targeted view.")]
    public string? ViewName { get; }

    protected ICommandContext Context { get; }

    public override async Task<int> OnExecuteAsync()
    {
        try
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
        catch (OutputException exception)
        {
            return exception.ExitCode;
        }
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
