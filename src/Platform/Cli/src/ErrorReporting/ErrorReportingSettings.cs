using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Cli.ErrorReporting;

// What ErrorReporter needs from the host, read in InitializeHost: the host is disposed before a failure reaches Main.
internal sealed record ErrorReportingSettings
{
    // The variables whose values Sentry never shows: the address, the client, the files and the home of a machine or a
    // session. Not the switches (SFORM_TOKEN_STORE=file, SFORM_TELEMETRY_OPTOUT=1, …), whose words would cut every message.
    private static readonly string[] ContextVariables =
    [
        StorytellerVariables.ENDPOINT,
        StorytellerVariables.API_URL,
        StorytellerVariables.CLIENT_ID,
        StorytellerVariables.TOKEN_URL,
        StorytellerVariables.TOKEN_SCOPE,
        StorytellerVariables.KEY,
        StorytellerVariables.CLIENT_CERTIFICATE,
        StorytellerVariables.CERTIFICATE_FILE,
        StorytellerVariables.TOKEN_FILE,
        StorytellerVariables.SUBJECT_TOKEN_FILE,
        StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL,
        SformVariables.HOME,
    ];

    // The variables of secrets, which the local log never shows either.
    private static readonly string[] SecretVariables =
    [
        StorytellerVariables.API_KEY,
        StorytellerVariables.CLIENT_SECRET,
        StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD,
        StorytellerVariables.CERTIFICATE_PASSWORD,
        StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN,
    ];

    // A setting of SFORM_<section>__<key> with one of these values is a switch, not a name.
    private static readonly HashSet<string> SwitchValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "on", "off", "file", "json", "yaml", "none", "default",
    };

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
    // target and the API key's machine of a machine, the address, client and files of its variables, SFORM_HOME, the
    // settings of SFORM_<section>__<key>, the user, the machine and the profile folder.
    public IReadOnlyList<string?> ContextValues { get; init; } = [];

    // The secrets of the variables (STORYTELLER_CLIENT_SECRET, …): neither the local log nor Sentry shows them.
    public IReadOnlyList<string?> SecretValues { get; init; } = [];

    // Read when a report is made: the organizations, projects and machines the credential named while it loaded.
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

        try
        {
            if (machine?.Key is { } key)
            {
                context.AddRange([key.OrganizationName, key.ProjectName, key.ViewName, key.Annotation.ToString()]);
            }

            // Without regard to case, as Windows reads them; of two that differ only in case (http_proxy and HTTP_PROXY on
            // Linux) the first is kept.
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry variable in environmentVariables ?? new Hashtable())
            {
                if (variable is { Key: string name, Value: string value })
                {
                    variables.TryAdd(name, value);
                }
            }

            context.AddRange(ContextVariables.Select(name => variables.GetValueOrDefault(name)));
            secrets.AddRange(SecretVariables.Select(name => variables.GetValueOrDefault(name)));

            // The machine an API key names, before the credential loads; a certificate is known only once it did.
            if (MachineApiKey.TryParse(variables.GetValueOrDefault(StorytellerVariables.API_KEY)) is { } apiKey)
            {
                context.AddRange([apiKey.Organization, apiKey.Project, apiKey.MachineId]);
            }

            context.AddRange(variables
                .Where(variable => variable.Key.StartsWith(SformVariables.CONFIGURATION_PREFIX, StringComparison.OrdinalIgnoreCase)
                    && variable.Key.Contains("__", StringComparison.Ordinal))
                .Select(variable => variable.Value.Trim())
                .Where(value => value.Length >= 4 && !SwitchValues.Contains(value) && !double.TryParse(value, out _)));
        }
        catch (Exception)
        {
            // What was read so far is kept.
        }

        try
        {
            context.AddRange(person);
        }
        catch (Exception)
        {
            // The person is optional too.
        }

        return new ErrorReportingSettings
        {
            Dsn = logging?.SentryDsn,
            IsOptedOut = isOptedOut,
            InstallationId = installationId,
            Environment = environmentName,
            Release = release,
            ContextValues = context.Where(value => !string.IsNullOrWhiteSpace(value)).ToList().AsReadOnly(),
            SecretValues = secrets.Where(value => !string.IsNullOrWhiteSpace(value)).ToList().AsReadOnly(),
            ReportTimeValues = machineSignIn is null ? null : () => IdentityValuesOf(machineSignIn),
        };
    }

    private static IEnumerable<string?> IdentityValuesOf(IMachineSignIn machineSignIn)
    {
        var identity = machineSignIn.TryGetIdentity();
        IEnumerable<string?> names = identity is null ? [] : [identity.Organization, identity.Project, identity.MachineId];
        return names.Concat(machineSignIn.KnownNames ?? []);
    }
}
