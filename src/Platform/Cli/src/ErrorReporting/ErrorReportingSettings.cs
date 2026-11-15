using System.Collections.Generic;

namespace _42.Platform.Cli.ErrorReporting;

// What ErrorReporter needs from the host, read in InitializeHost: the host is disposed before a failure reaches Main.
// Dsn: logging.sentryDsn; blank, or with IsOptedOut (DO_NOT_TRACK, SFORM_TELEMETRY_OPTOUT), nothing goes to Sentry.
// InstallationId: the id of the usage event when telemetry is on, the only id a report carries.
// Environment and Release: set in code, so the SENTRY_* variables of a machine do not change them.
// ContextValues: the default organization, project and view, and STORYTELLER_PROJECT and STORYTELLER_VIEW of a machine;
// Sentry never shows them, though they were not given on the command line.
internal sealed record ErrorReportingSettings(
    string? Dsn,
    bool IsOptedOut,
    string? InstallationId,
    string Environment,
    string Release,
    IReadOnlyList<string?> ContextValues);
