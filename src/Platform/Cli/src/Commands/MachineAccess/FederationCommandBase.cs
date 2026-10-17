using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Every federation command (trusted issuers, federation rules): the access API, and API errors turned into
// the server's message and an exit code.
public abstract class FederationCommandBase : BaseContextCommand
{
    protected FederationCommandBase(IExtendedConsole console, ICommandContext context, IAccessApiClient accessApi)
        : base(console, context)
    {
        AccessApi = accessApi;
    }

    protected IAccessApiClient AccessApi { get; }

    protected sealed override async Task<int> ExecuteAsync()
    {
        try
        {
            return await ExecuteFederationAsync();
        }
        catch (ApiException exception)
        {
            return Console.WriteApiError(exception);
        }
    }

    protected abstract Task<int> ExecuteFederationAsync();
}
