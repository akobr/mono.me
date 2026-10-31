using System.IO.Abstractions;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

public sealed class ReportingDispatcherOptions
{
    public int BatchSize { get; set; } = 20;

    // A batch is sent once it is full or this long after its first result arrived.
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    // Attempts of a call that failed transiently; the delay doubles from InitialBackoff up to MaxBackoff.
    public int MaxAttempts { get; set; } = 5;

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

    // Bounds the start of each reporter, the drain at the end, and the completion of each reporter, each on its own.
    public TimeSpan CompleteTimeout { get; set; } = TimeSpan.FromSeconds(120);

    public UnmappedTestsPolicy UnmappedTests { get; set; } = UnmappedTestsPolicy.Ignore;

    // results.jsonl; no file when null.
    public string? ResultsFilePath { get; set; }

    // A FileSystem over System.IO when unset.
    public IFileSystem? FileSystem { get; set; }

    // TimeProvider.System when unset.
    public TimeProvider? TimeProvider { get; set; }

    public static ReportingDispatcherOptions FromSettings(ReportingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ReportingDispatcherOptions
        {
            BatchSize = settings.BatchSize,
            FlushInterval = TimeSpan.FromSeconds(settings.FlushIntervalSeconds),
            MaxAttempts = settings.MaxAttempts,
            CompleteTimeout = TimeSpan.FromSeconds(settings.CompleteTimeoutSeconds),
            UnmappedTests = settings.UnmappedTests,
        };
    }
}
