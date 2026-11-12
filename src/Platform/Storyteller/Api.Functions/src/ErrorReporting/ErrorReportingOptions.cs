namespace _42.Platform.Storyteller.Api.ErrorReporting;

// The section Sentry (app settings Sentry__Dsn, …). Only the DSN and the volume come from configuration; what an event
// may carry is fixed in code (ErrorReportingEntryPoint, SentryScrubber). See docs/Platform/Storyteller/error-reporting.md.
public sealed class ErrorReportingOptions
{
    public const string SectionName = "Sentry";

    // The DSN of the API's Sentry project. Empty, blank or malformed: nothing is registered and nothing is sent.
    public string? Dsn { get; set; }

    // The same error is sent once in this interval; zero sends every repeat.
    public TimeSpan RepeatInterval { get; set; } = TimeSpan.FromHours(1);

    // The events an instance sends in a clock hour; zero is no limit. An error the instance has not sent before passes it.
    public int MaxEventsPerHour { get; set; } = 10;

    // The events an instance sends in a UTC day; zero is no limit.
    public int MaxEventsPerDay { get; set; } = 40;
}
