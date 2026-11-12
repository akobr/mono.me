using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// A malformed Sentry setting doesn't stop the API (a DSN turns error reporting off, a volume setting keeps its default);
// this says so once at start. The messages name the setting, never its value.
internal sealed class ErrorReportingWarnings : IHostedService
{
    private readonly ILogger<ErrorReportingWarnings> _logger;
    private readonly IReadOnlyList<string> _messages;

    public ErrorReportingWarnings(ILogger<ErrorReportingWarnings> logger, IReadOnlyList<string> messages)
    {
        _logger = logger;
        _messages = messages;
    }

    public IReadOnlyList<string> Messages => _messages;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var message in _messages)
        {
#pragma warning disable CA2254 // The messages are fixed texts of ErrorReportingEntryPoint, without values.
            _logger.LogWarning(message);
#pragma warning restore CA2254
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
