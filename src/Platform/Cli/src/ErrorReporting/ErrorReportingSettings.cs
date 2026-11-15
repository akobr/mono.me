using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;

namespace _42.Platform.Cli.ErrorReporting;

// What ErrorReporter needs from the host, read in InitializeHost: the host is disposed before a failure reaches Main.
internal sealed record ErrorReportingSettings
{
    // The prefixes of the variables whose values Sentry never shows: the machine's credentials and target, SFORM_HOME and
    // the settings of the environment.
    private static readonly string[] ContextVariablePrefixes = ["STORYTELLER_", "SFORM_"];

    // The words of the names of variables that hold a secret, which the local log never shows either.
    private static readonly string[] SecretVariableWords = ["SECRET", "PASSWORD", "API_KEY", "REQUEST_TOKEN"];

    // logging.sentryDsn; blank, or with IsOptedOut, nothing goes to Sentry.
    public string? Dsn { get; init; }

    // DO_NOT_TRACK or SFORM_TELEMETRY_OPTOUT.
    public bool IsOptedOut { get; init; }

    // The id of the usage event when telemetry is on, the only id a report carries.
    public string? InstallationId { get; init; }

    // Set in code, so the SENTRY_* variables of a machine do not change them.
    public string Environment { get; init; } = "production";

    public string Release { get; init; } = "42.Platform.Cli@unknown";

    // What Sentry never shows, though the command line did not give it: the default organization, project and view, the
    // target and credentials of a machine, the values of the STORYTELLER_* and SFORM_* variables, the user and the machine.
    public IReadOnlyList<string?> ContextValues { get; init; } = [];

    // The secrets of the environment (STORYTELLER_CLIENT_SECRET, …): neither the local log nor Sentry shows them.
    public IReadOnlyList<string?> SecretValues { get; init; } = [];

    // Read when a report is made: the organization, project and machine of the credential, known once it was loaded.
    public Func<IEnumerable<string?>>? ReportTimeValues { get; init; }

    // From what InitializeHost captured. Never throws: a value that cannot be read is left out.
    public static ErrorReportingSettings Create(
        LoggingOptions? logging,
        bool isOptedOut,
        string? installationId,
        string environmentName,
        string release,
        AccessDefaultOptions? access,
        MachineSignInSettings? machine,
        IMachineSignIn? machineSignIn,
        IDictionary? environmentVariables,
        IEnumerable<string?> person)
    {
        var context = new List<string?> { access?.OrganizationName, access?.ProjectName, access?.ViewName, machine?.Project, machine?.View };
        var secrets = new List<string?>();

        if (machine?.Key is { } key)
        {
            context.AddRange([key.OrganizationName, key.ProjectName, key.ViewName, key.Annotation.ToString()]);
        }

        try
        {
            foreach (DictionaryEntry variable in environmentVariables ?? new Hashtable())
            {
                if (variable.Key is not string name
                    || variable.Value is not string value
                    || !ContextVariablePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                context.Add(value);

                if (SecretVariableWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    secrets.Add(value);
                }
            }

            context.AddRange(person);
        }
        catch (Exception)
        {
            // What was read so far is kept.
        }

        return new ErrorReportingSettings
        {
            Dsn = logging?.SentryDsn,
            IsOptedOut = isOptedOut,
            InstallationId = installationId,
            Environment = environmentName,
            Release = release,
            ContextValues = context.AsReadOnly(),
            SecretValues = secrets.AsReadOnly(),
            ReportTimeValues = machineSignIn is null ? null : () => IdentityValuesOf(machineSignIn),
        };
    }

    private static IEnumerable<string?> IdentityValuesOf(IMachineSignIn machineSignIn)
    {
        var identity = machineSignIn.TryGetIdentity();
        return identity is null ? [] : [identity.Organization, identity.Project, identity.MachineId];
    }
}
