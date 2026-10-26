using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller;

// Notes the account-id rule of Entra ID users once at start, so a deployment that switched Auth:Provider sees why its
// old accounts no longer resolve. Logging only, without I/O.
internal sealed class EntraIdAccountIdNotice : IHostedService
{
    private readonly ILogger<EntraIdAccountIdNotice> _logger;

    public EntraIdAccountIdNotice(ILogger<EntraIdAccountIdNotice> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Entra ID accounts are keyed by {AccountIdForm} of the user token. Accounts registered under another identity provider, or under the pairwise Entra sub, do not resolve.",
            "{tid}:{oid}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
