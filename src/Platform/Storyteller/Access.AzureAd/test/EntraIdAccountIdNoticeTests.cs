using System.Collections.Concurrent;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class EntraIdAccountIdNoticeTests
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Auth:Provider"] = "EntraId",
        ["Auth:TenantId"] = "tenant-id",
        ["Auth:ClientId"] = "client-id",
        ["MachineAuth:AzureAd:TenantId"] = "machine-tenant",
    };

    // A deployment that switched Auth:Provider sees once why its old accounts no longer resolve.
    [Fact]
    public async Task AddEntraIdUserAuthentication_LogsTheAccountIdRuleOnceAtStart()
    {
        var logs = new RecordingLoggerProvider();
        using var host = BuildHost(logs, services =>
        {
            services.AddEntraIdUserAuthentication(new ConfigurationBuilder().AddInMemoryCollection(Settings).Build());
            services.AddEntraIdUserAuthentication(new ConfigurationBuilder().AddInMemoryCollection(Settings).Build());
        });

        await host.StartAsync();

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("{tid}:{oid}");
        entry.Message.ShouldContain("another identity provider");

        await host.StopAsync();
    }

    [Fact]
    public async Task AddAzureAdMachineAccess_WithoutEntraUsers_LogsNoAccountIdRule()
    {
        var logs = new RecordingLoggerProvider();
        var settings = new Dictionary<string, string?>(Settings)
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = "client_123",
        };
        using var host = BuildHost(logs, services => services.AddAzureAdMachineAccess(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));

        await host.StartAsync();

        logs.Entries.ShouldBeEmpty();

        await host.StopAsync();
    }

    private static IHost BuildHost(RecordingLoggerProvider logs, Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        configure(builder.Services);
        return builder.Build();
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    // Records the entries of the notice only; the host logs its own start too.
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName)
        {
            return categoryName == "_42.Platform.Storyteller.EntraIdAccountIdNotice"
                ? new RecordingLogger(_entries)
                : NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<LogEntry> _entries;

        public RecordingLogger(ConcurrentQueue<LogEntry> entries)
        {
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
