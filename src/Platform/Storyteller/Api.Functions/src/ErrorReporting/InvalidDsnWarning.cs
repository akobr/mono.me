using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// A malformed Sentry:Dsn turns error reporting off instead of stopping the API; this says so once, without the value.
internal sealed class InvalidDsnWarning : IHostedService
{
    private readonly ILogger<InvalidDsnWarning> _logger;

    public InvalidDsnWarning(ILogger<InvalidDsnWarning> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Sentry:Dsn is not a valid DSN; errors are not reported to Sentry.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
