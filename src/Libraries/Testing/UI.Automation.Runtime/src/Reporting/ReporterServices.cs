using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// The services a reporter provider gets: the consumer's (or none), with IConfiguration answered by the run's own
// configuration, so a reporter can read the shared Reporting keys (e.g. Reporting:UnmappedTests) next to its section,
// and ILoggerFactory, when the consumer has none, by the session's logger, so a reporter's log reaches the terminal.
internal sealed class ReporterServices(IServiceProvider services, IConfiguration configuration, ILogger logger) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IConfiguration))
        {
            return configuration;
        }

        var service = services.GetService(serviceType);
        return service is null && serviceType == typeof(ILoggerFactory) ? new SessionLoggerFactory(logger) : service;
    }

    // Every category writes to the session's logger.
    private sealed class SessionLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            return logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }
}
