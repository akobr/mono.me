using _42.Platform.Storyteller.Api.ErrorReporting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sentry;
using Sentry.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The registration of Program.cs: nothing of Sentry without a valid DSN, and options no setting can weaken with one.
[Collection(SentrySdkCollection.Name)]
public class ErrorReportingEntryPointTests
{
    public static TheoryData<string> MalformedDsns => new()
    {
        "not a dsn",
        "https://o1.ingest.de.sentry.io/1",          // no public key
        "https://public@o1.ingest.de.sentry.io/",    // no project id
        "https://public@o1.ingest.de.sentry.io/1/",  // Sentry reads an empty project id after the last '/'
        "https://public@o1.ingest.de.sentry.io/1#/",
        "ftp://public@o1.ingest.de.sentry.io/1",
    };

    // DSNs Sentry itself parses: each must start the SDK without an exception.
    public static TheoryData<string> ValidDsns => new()
    {
        ErrorReportingHarness.Dsn,
        $"  {ErrorReportingHarness.Dsn}  ",
        "https://public:secret@o1.ingest.de.sentry.io/1",
        "https://public@sentry.example.com/prefix/42",
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddErrorReporting_WithoutADsn_RegistersNothing(string? dsn)
    {
        var configuration = Configuration(dsn);
        var services = new ServiceCollection();

        services.AddErrorReporting(configuration, new TestHostEnvironment("Production"));

        services.ShouldBeEmpty();
        configuration.IsErrorReportingEnabled().ShouldBeFalse();
    }

    [Fact]
    public void AddErrorReporting_WithoutADsn_IgnoresMalformedVolumeSettings()
    {
        var configuration = Configuration(null, ("Sentry:RepeatInterval", "1h"), ("Sentry:MaxEventsPerDay", "forty"));
        var services = new ServiceCollection();

        services.AddErrorReporting(configuration, new TestHostEnvironment("Production"));

        services.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(MalformedDsns))]
    public void AddErrorReporting_AMalformedDsn_OnlyWarnsAtStartup(string dsn)
    {
        var configuration = Configuration(dsn);
        var services = new ServiceCollection();

        services.AddErrorReporting(configuration, new TestHostEnvironment("Production"));

        services.ShouldHaveSingleItem().ServiceType.ShouldBe(typeof(IHostedService));
        Warnings(services).ShouldBe(["Sentry:Dsn is not a valid DSN; errors are not reported to Sentry."]);
        configuration.IsErrorReportingEnabled().ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(ValidDsns))]
    public async Task AddErrorReporting_AValidDsn_StartsTheSdk(string dsn)
    {
        var configuration = Configuration(dsn);
        configuration.IsErrorReportingEnabled().ShouldBeTrue();

        var services = new ServiceCollection()
            .AddErrorReporting(configuration, new TestHostEnvironment("Production"), ErrorReportingHarness.ConfigureForTests(new RecordingTransport()));

        await using var provider = services.BuildServiceProvider();

        try
        {
            // the logger factory builds the Sentry provider, which parses the DSN
            Should.NotThrow(() => provider.GetRequiredService<ILoggerFactory>());
        }
        finally
        {
            SentrySdk.Close();
        }
    }

    [Fact]
    public void AddErrorReporting_MalformedVolumeSettings_KeepTheirDefaultsAndWarn()
    {
        var configuration = Configuration(
            ErrorReportingHarness.Dsn,
            ("Sentry:RepeatInterval", "1h"),
            ("Sentry:MaxEventsPerHour", "-5"),
            ("Sentry:MaxEventsPerDay", "forty"));
        var services = new ServiceCollection();

        services.AddErrorReporting(configuration, new TestHostEnvironment("Production"), ErrorReportingHarness.ConfigureForTests(new RecordingTransport()));

        Warnings(services).ShouldBe(
        [
            "Sentry:RepeatInterval is not valid; its default is used.",
            "Sentry:MaxEventsPerHour is not valid; its default is used.",
            "Sentry:MaxEventsPerDay is not valid; its default is used.",
        ]);

        var (options, invalid) = ErrorReportingEntryPoint.ReadOptions(configuration.GetSection("Sentry"));
        invalid.Count.ShouldBe(3);
        options.RepeatInterval.ShouldBe(TimeSpan.FromHours(1));
        options.MaxEventsPerHour.ShouldBe(10);
        options.MaxEventsPerDay.ShouldBe(40);
    }

    [Fact]
    public void ReadOptions_ValidVolumeSettings_AreUsed()
    {
        var configuration = Configuration(
            ErrorReportingHarness.Dsn,
            ("Sentry:RepeatInterval", "00:30:00"),
            ("Sentry:MaxEventsPerHour", "0"),
            ("Sentry:MaxEventsPerDay", "100"));

        var (options, invalid) = ErrorReportingEntryPoint.ReadOptions(configuration.GetSection("Sentry"));

        invalid.ShouldBeEmpty();
        options.RepeatInterval.ShouldBe(TimeSpan.FromMinutes(30));
        options.MaxEventsPerHour.ShouldBe(0);
        options.MaxEventsPerDay.ShouldBe(100);
    }

    [Fact]
    public async Task ErrorReportingWarnings_Start_WarnsEachMessage()
    {
        var logger = new CapturingLogger<ErrorReportingWarnings>();

        await new ErrorReportingWarnings(logger, ["first", "second"]).StartAsync(TestContext.Current.CancellationToken);

        logger.Entries.Select(entry => (entry.Level, entry.Message)).ShouldBe([(LogLevel.Warning, "first"), (LogLevel.Warning, "second")]);
    }

    [Fact]
    public void IsErrorReportingEnabled_AValidDsn_IsTrue()
    {
        Configuration(ErrorReportingHarness.Dsn).IsErrorReportingEnabled().ShouldBeTrue();
        Configuration($"  {ErrorReportingHarness.Dsn}  ").IsErrorReportingEnabled().ShouldBeTrue();
    }

    [Fact]
    public void AddErrorReporting_ALaterConfiguration_CannotWeakenTheOptions()
    {
        var services = new ServiceCollection()
            .AddErrorReporting(Configuration(ErrorReportingHarness.Dsn), new TestHostEnvironment("Production"));
        services.Configure<SentryLoggingOptions>(options =>
        {
            options.SendDefaultPii = true;
            options.CaptureFailedRequests = true;
            options.MinimumEventLevel = LogLevel.Debug;
        });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SentryLoggingOptions>>().Value;

        options.SendDefaultPii.ShouldBeFalse();
        options.CaptureFailedRequests.ShouldBeFalse();
        options.MinimumEventLevel.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public void AddErrorReporting_WithADsn_FixesWhatAnEventMayCarry()
    {
        using var services = new ServiceCollection()
            .AddErrorReporting(Configuration($" {ErrorReportingHarness.Dsn} "), new TestHostEnvironment("Production"))
            .BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<SentryLoggingOptions>>().Value;

        options.Dsn.ShouldBe(ErrorReportingHarness.Dsn);
        options.Environment.ShouldBe("production");
        options.Release.ShouldBe(ErrorReportingEntryPoint.GetRelease());
        options.Release.ShouldStartWith("42.Platform.Storyteller.Api.Functions@");
        options.MinimumEventLevel.ShouldBe(LogLevel.Error);
        options.MinimumBreadcrumbLevel.ShouldBe(LogLevel.Information);
        options.MaxBreadcrumbs.ShouldBe(30);
        options.SendDefaultPii.ShouldBeFalse();
        options.IsEnvironmentUser.ShouldBeFalse();
        options.CaptureFailedRequests.ShouldBeFalse();
        options.DisableSentryHttpMessageHandler.ShouldBeTrue();
        options.TracesSampleRate.ShouldBeNull();
        options.TracesSampler.ShouldBeNull();
        options.AutoSessionTracking.ShouldBeFalse();
        options.EnableLogs.ShouldBeFalse();
    }

    [Fact]
    public void AddErrorReporting_LoggingSentrySettings_CannotTurnOnPersonalData()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sentry:Dsn"] = ErrorReportingHarness.Dsn,
                ["Logging:Sentry:SendDefaultPii"] = "true",
                ["Logging:Sentry:CaptureFailedRequests"] = "true",
                ["Logging:Sentry:MinimumEventLevel"] = "Information",
                ["Logging:Sentry:Debug"] = "true", // the control: Logging:Sentry is bound
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
        services.AddErrorReporting(configuration, new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SentryLoggingOptions>>().Value;

        options.Debug.ShouldBeTrue();
        options.SendDefaultPii.ShouldBeFalse();
        options.CaptureFailedRequests.ShouldBeFalse();
        options.MinimumEventLevel.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public void AddErrorReporting_TheEnvironment_IsTheHostsInLowerCase()
    {
        using var services = new ServiceCollection()
            .AddErrorReporting(Configuration(ErrorReportingHarness.Dsn), new TestHostEnvironment("Staging"))
            .BuildServiceProvider();

        services.GetRequiredService<IOptions<SentryLoggingOptions>>().Value.Environment.ShouldBe("staging");
    }

    [Fact]
    public async Task AddErrorReporting_WithApplicationInsights_KeepsBothLoggers()
    {
        var transport = new RecordingTransport();
        var services = new ServiceCollection();
        services.AddApplicationInsightsTelemetryWorkerService();
        services.AddErrorReporting(
            Configuration(ErrorReportingHarness.Dsn),
            new TestHostEnvironment("Production"),
            ErrorReportingHarness.ConfigureForTests(transport));

        await using var provider = services.BuildServiceProvider();

        try
        {
            var providers = provider.GetServices<ILoggerProvider>().Select(logger => logger.GetType().Name).ToList();
            providers.ShouldContain("ApplicationInsightsLoggerProvider");
            providers.ShouldContain("SentryLoggerProvider");
        }
        finally
        {
            SentrySdk.Close();
        }
    }

    [Theory]
    [InlineData("_42.Platform.Storyteller.Api.V1.KeyValuesHttp", LogLevel.Information, false)]
    [InlineData("_42.Platform.Storyteller.Api.V1.KeyValuesHttp", LogLevel.Error, false)]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information, true)]
    [InlineData("System.Net.Http.HttpClient.Default.LogicalHandler", LogLevel.Information, true)]
    [InlineData("Azure.Core", LogLevel.Information, true)]
    [InlineData("Azure.Core", LogLevel.Warning, false)]
    [InlineData("Microsoft.Azure.Functions.Worker.FunctionsApplication", LogLevel.Error, false)]
    public void IsFilteredOut_ByCategoryAndLevel(string category, LogLevel level, bool expected)
    {
        ErrorReportingEntryPoint.IsFilteredOut(category, level, default, exception: null).ShouldBe(expected);
    }

    [Fact]
    public void IsFilteredOut_ACancellationTheWorkerLogs_IsDropped()
    {
        ErrorReportingEntryPoint
            .IsFilteredOut("Microsoft.Azure.Functions.Worker.FunctionsApplication", LogLevel.Error, default, new OperationCanceledException())
            .ShouldBeTrue();
        ErrorReportingEntryPoint
            .IsFilteredOut("_42.Platform.Storyteller.Api.ErrorHandling.ExceptionHandlingMiddleware", LogLevel.Error, default, new OperationCanceledException())
            .ShouldBeFalse();
    }

    private static IConfiguration Configuration(string? dsn, params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?> { ["Sentry:Dsn"] = dsn };

        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // The messages of the start warning, as the host would run it.
    private static List<string> Warnings(IServiceCollection services)
    {
        IServiceCollection all = new ServiceCollection();

        foreach (var service in services)
        {
            all.Add(service);
        }

        using var provider = all.AddLogging().BuildServiceProvider();

        try
        {
            return provider.GetServices<IHostedService>().OfType<ErrorReportingWarnings>().SelectMany(warnings => warnings.Messages).ToList();
        }
        finally
        {
            // a valid DSN starts the SDK with the logger factory
            SentrySdk.Close();
        }
    }
}
