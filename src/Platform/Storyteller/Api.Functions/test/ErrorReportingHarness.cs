using _42.Platform.Storyteller.Api.ErrorReporting;

using Azure.Core.Serialization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sentry;
using Sentry.Extensibility;
using Sentry.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The API's error reporting as Program.cs registers it, with a fake DSN and the recording transport; the throttle is
// off unless the test sets it. Disposing it closes the SDK.
internal sealed class ErrorReportingHarness : IAsyncDisposable
{
    public const string Dsn = "https://public@o1.ingest.de.sentry.io/1";

    private readonly ServiceProvider _services;

    private ErrorReportingHarness(ServiceProvider services, RecordingTransport transport)
    {
        _services = services;
        Transport = transport;
        Hub = services.GetRequiredService<IHub>();
        Loggers = services.GetRequiredService<ILoggerFactory>();
    }

    public RecordingTransport Transport { get; }

    public IHub Hub { get; }

    public ILoggerFactory Loggers { get; }

    public IServiceProvider Services => _services;

    public static ErrorReportingHarness Start(IDictionary<string, string?>? settings = null, string environment = "Production")
    {
        var values = new Dictionary<string, string?>
        {
            ["Sentry:Dsn"] = Dsn,
            ["Sentry:RepeatInterval"] = "00:00:00",
            ["Sentry:MaxEventsPerHour"] = "0",
            ["Sentry:MaxEventsPerDay"] = "0",
        };

        foreach (var setting in settings ?? new Dictionary<string, string?>())
        {
            values[setting.Key] = setting.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var transport = new RecordingTransport();
        var services = new ServiceCollection()

            // what the 500 answer of ExceptionHandlingMiddleware is written with
            .AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }))
            .AddErrorReporting(configuration, new TestHostEnvironment(environment), ConfigureForTests(transport))
            .BuildServiceProvider();

        return new ErrorReportingHarness(services, transport);
    }

    // Nothing of the test process (unhandled or unobserved exceptions of other tests) and no network.
    public static Action<SentryLoggingOptions> ConfigureForTests(ITransport transport)
    {
        return options =>
        {
            options.Transport = transport;
            options.DisableAppDomainUnhandledExceptionCapture();
            options.DisableUnobservedTaskExceptionCapture();
            options.DisableAppDomainProcessExitFlush();
            options.SendClientReports = false;
        };
    }

    public ILogger Logger(string category) => Loggers.CreateLogger(category);

    public async Task FlushAsync()
    {
        await Hub.FlushAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync();
        await _services.DisposeAsync();
        SentrySdk.Close();
    }
}
