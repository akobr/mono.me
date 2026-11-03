using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Configuring.Container;

// The provider starts before the container and its logger factory exist. At the start of the host, the container's
// factory goes to every engine that was given none, which then reports a degraded start; at the end it goes back,
// because the host disposes the factory before the configuration.
internal sealed class StorytellerLoggingHandover : IHostedLifecycleService
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private IReadOnlyList<StorytellerConfigurationEngine> _engines = [];

    public StorytellerLoggingHandover(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _engines = StorytellerEngines.Find(_configuration);

        foreach (var engine in _engines)
        {
            engine.UseLoggerFactory(_loggerFactory);
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        // Also the engines of sources added since the start.
        foreach (var engine in _engines.Union(StorytellerEngines.Find(_configuration)))
        {
            engine.ReleaseLoggerFactory();
        }

        return Task.CompletedTask;
    }
}
