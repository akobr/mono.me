using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// Runs the real rule selection of Microsoft.Extensions.Logging over clamped options.
public class LoggingSafetyTests
{
    private const string ServerCategory = "ModelContextProtocol.Server.McpServer";

    [Fact]
    public void Clamp_DefaultTraceRule_NoLongerReachesMcpCategories()
    {
        var options = Options(new LoggerFilterRule(null, null, LogLevel.Trace, null));

        IsEnabled(options, ServerCategory, LogLevel.Trace).ShouldBeFalse();
        IsEnabled(options, ServerCategory, LogLevel.Information).ShouldBeTrue();
        IsEnabled(options, "Other.Category", LogLevel.Trace).ShouldBeTrue();
    }

    [Fact]
    public void Clamp_ProviderTraceRule_IsCoveredForThatProvider()
    {
        var options = Options(new LoggerFilterRule(TestProvider.Alias, null, LogLevel.Trace, null));

        IsEnabled(options, ServerCategory, LogLevel.Trace).ShouldBeFalse();
        IsEnabled(options, "Other.Category", LogLevel.Trace).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "ModelContextProtocol")]
    [InlineData(null, "ModelContextProtocol.Server")]
    [InlineData(TestProvider.Alias, "ModelContextProtocol.Server")]
    [InlineData(TestProvider.Alias, "ModelContextProtocol*")]
    [InlineData(TestProvider.Alias, "Model*")]
    [InlineData(TestProvider.Alias, "*McpServer")]
    public void Clamp_TraceRuleThatMatchesMcp_IsRaised(string? provider, string category)
    {
        var options = Options(new LoggerFilterRule(provider, category, LogLevel.Trace, null));

        IsEnabled(options, ServerCategory, LogLevel.Trace).ShouldBeFalse();
        IsEnabled(options, ServerCategory, LogLevel.Debug).ShouldBeFalse();
        IsEnabled(options, ServerCategory, LogLevel.Information).ShouldBeTrue();
    }

    [Fact]
    public void Clamp_StricterRule_StaysStricter()
    {
        var options = Options(new LoggerFilterRule(null, "ModelContextProtocol.Server", LogLevel.Error, null));

        IsEnabled(options, ServerCategory, LogLevel.Warning).ShouldBeFalse();
        IsEnabled(options, ServerCategory, LogLevel.Error).ShouldBeTrue();
    }

    [Fact]
    public void Clamp_FilterRule_IsWrapped()
    {
        var options = Options(new LoggerFilterRule(TestProvider.Alias, "ModelContextProtocol.Server", null, (_, _, _) => true));

        IsEnabled(options, ServerCategory, LogLevel.Trace).ShouldBeFalse();
        IsEnabled(options, ServerCategory, LogLevel.Warning).ShouldBeTrue();
    }

    [Fact]
    public void Clamp_LowMinimumLevel_DoesNotReachMcpCategories()
    {
        var options = new LoggerFilterOptions { MinLevel = LogLevel.Trace };
        LoggingSafety.Clamp(options);

        IsEnabled(options, ServerCategory, LogLevel.Trace).ShouldBeFalse();
    }

    private static LoggerFilterOptions Options(LoggerFilterRule rule)
    {
        var options = new LoggerFilterOptions { MinLevel = LogLevel.Trace };
        options.Rules.Add(rule);
        LoggingSafety.Clamp(options);
        return options;
    }

    private static bool IsEnabled(LoggerFilterOptions options, string category, LogLevel level)
    {
        using var factory = new LoggerFactory([new TestProvider()], new StaticMonitor(options));
        return factory.CreateLogger(category).IsEnabled(level);
    }

    [ProviderAlias(Alias)]
    private sealed class TestProvider : ILoggerProvider
    {
        public const string Alias = "Test";

        public ILogger CreateLogger(string categoryName) => new NullLogger();

        public void Dispose()
        {
        }
    }

    private sealed class NullLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class StaticMonitor(LoggerFilterOptions options) : IOptionsMonitor<LoggerFilterOptions>
    {
        public LoggerFilterOptions CurrentValue => options;

        public LoggerFilterOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<LoggerFilterOptions, string?> listener) => null;
    }
}
